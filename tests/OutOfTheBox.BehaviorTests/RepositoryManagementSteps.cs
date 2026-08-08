// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.BehaviorTests.Support;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>RepositoryManagement.feature</c>.</summary>
[Binding]
public sealed class RepositoryManagementSteps : IDisposable
{
    private GitFixture? _gitFixture;
    private CommandExecutionServiceFactory? _factory;
    private IServiceScope? _scope;
    private RepositoryActionResult? _cloneResult;
    private RepositoryActionResult? _deleteResult;
    private HttpResponseMessage? _httpResponse;
    private Guid _inFlightRunId;
    private CancellationTokenSource? _inFlightCts;
    private string _inFlightTargetPath = string.Empty;
    private FileStream? _lockedFile;
    private const string TargetName = "cloned-repository";

    private async Task EnsureFactoryAsync()
    {
        if (_factory is not null)
        {
            return;
        }

        _gitFixture = await GitFixture.CreateAsync();
        _factory = new CommandExecutionServiceFactory(rootDirectoryOverride: _gitFixture.RootDirectory);
        _scope = _factory.Services.CreateScope();
    }

    private string SourceRepositoryPath => Path.Combine(_gitFixture!.RootDirectory, "GitFixture");

    private IRepositoryManager RepositoryManager => _scope!.ServiceProvider.GetRequiredService<IRepositoryManager>();

    private IRunRepository RunRepository => _scope!.ServiceProvider.GetRequiredService<IRunRepository>();

    private RunRegistry RunRegistry => _scope!.ServiceProvider.GetRequiredService<RunRegistry>();

    [When(@"an operator clones the fixture repository under a new name")]
    public async Task WhenAnOperatorClonesTheFixtureRepositoryUnderANewName()
    {
        await EnsureFactoryAsync();
        _cloneResult = await RepositoryManager.CloneAsync(SourceRepositoryPath, TargetName, CancellationToken.None);

        // The clone runs in the background (CloneAsync returns as soon as it's accepted) - poll
        // briefly for the terminal history row rather than assuming it's already there.
        await WaitForTerminalCloneAsync(TargetName);
    }

    [Then(@"the clone succeeds and the new repository appears in the repository list")]
    public async Task ThenTheCloneSucceedsAndTheNewRepositoryAppearsInTheRepositoryList()
    {
        Assert.IsType<RepositoryActionResult.Accepted>(_cloneResult);
        Assert.True(Directory.Exists(Path.Combine(_gitFixture!.RootDirectory, TargetName)));

        var summaries = await RepositoryManager.ListAsync(CancellationToken.None);
        Assert.Contains(summaries, r => r.Name == TargetName);
    }

    [Then(@"a history record exists for the clone with its source URL and a completed outcome")]
    public async Task ThenAHistoryRecordExistsForTheCloneWithItsSourceUrlAndACompletedOutcome()
    {
        var accepted = Assert.IsType<RepositoryActionResult.Accepted>(_cloneResult);
        var run = await RunRepository.FindByIdAsync(accepted.RunId, CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(RunKind.RepositoryClone, run.Kind);
        Assert.Equal(SourceRepositoryPath, run.SourceUrl);
        Assert.Equal(RunOutcome.Completed, run.Outcome);
    }

    [Given(@"a repository already exists under a given name")]
    public async Task GivenARepositoryAlreadyExistsUnderAGivenName()
    {
        await EnsureFactoryAsync();
        Directory.CreateDirectory(Path.Combine(_gitFixture!.RootDirectory, TargetName));
        await File.WriteAllTextAsync(Path.Combine(_gitFixture.RootDirectory, TargetName, "marker.txt"), "pre-existing");
    }

    [When(@"an operator attempts to clone into that same name")]
    public async Task WhenAnOperatorAttemptsToCloneIntoThatSameName() => _cloneResult = await RepositoryManager.CloneAsync(SourceRepositoryPath, TargetName, CancellationToken.None);

    [Then(@"the clone is rejected as already existing")]
    public void ThenTheCloneIsRejectedAsAlreadyExisting()
    {
        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(_cloneResult);
        Assert.Equal(RepositoryActionRejectionReason.AlreadyExists, rejected.Reason);
    }

    [Then(@"the existing repository's contents are untouched")]
    public void ThenTheExistingRepositorySContentsAreUntouched() => Assert.True(File.Exists(Path.Combine(_gitFixture!.RootDirectory, TargetName, "marker.txt")));

    [Given(@"a clone into a given name is already in flight")]
    public async Task GivenACloneIntoAGivenNameIsAlreadyInFlight()
    {
        await EnsureFactoryAsync();

        // Deterministic rather than racing a real (near-instant, tiny) local clone to completion:
        // pre-acquires the exact lock CloneAsync itself would, via the same shared RunRegistry - a
        // real "second caller sees this repository as busy" state, without depending on process timing.
        _inFlightTargetPath = Path.Combine(_gitFixture!.RootDirectory, TargetName);
        _inFlightRunId = Guid.NewGuid();
        _inFlightCts = new CancellationTokenSource();
        RunRegistry.TryAcquire(_inFlightTargetPath, _inFlightRunId, _inFlightCts, out _);

        await RunRepository.AddAsync(new Run
        {
            Id = _inFlightRunId,
            Kind = RunKind.RepositoryClone,
            RepositoryPath = _inFlightTargetPath,
            SourceUrl = SourceRepositoryPath,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);
    }

    [When(@"a second clone into that same name is requested before the first finishes")]
    public async Task WhenASecondCloneIntoThatSameNameIsRequestedBeforeTheFirstFinishes() => _cloneResult = await RepositoryManager.CloneAsync(SourceRepositoryPath, TargetName, CancellationToken.None);

    [Then(@"the second clone is rejected as a conflict identifying the in-flight run")]
    public void ThenTheSecondCloneIsRejectedAsAConflictIdentifyingTheInFlightRun()
    {
        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(_cloneResult);
        Assert.Equal(RepositoryActionRejectionReason.Busy, rejected.Reason);
        Assert.Equal(_inFlightRunId, rejected.ConflictingRunId);
    }

    [When(@"a git command targets that same partially-cloned repository")]
    public async Task WhenAGitCommandTargetsThatSamePartiallyClonedRepository()
    {
        using var client = _factory!.CreateClient();
        var result = await SseTestClient.PostAndReadAllEventsAsync(
            client,
            "/run/git",
            new { arguments = new[] { "status" }, workingDirectory = TargetName },
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);

        _httpResponse = result.Response;
        _events = result.Events;
    }

    [Then(@"the command is rejected as a repository conflict")]
    public void ThenTheCommandIsRejectedAsARepoConflict()
    {
        var errorEvent = Assert.Single(_events, e => e.Name == "error");
        Assert.Contains(_inFlightRunId.ToString(), errorEvent.Data);
    }

    [Given(@"an idle repository exists")]
    public async Task GivenAnIdleRepositoryExists()
    {
        await EnsureFactoryAsync();
        Directory.CreateDirectory(Path.Combine(_gitFixture!.RootDirectory, TargetName));
        await File.WriteAllTextAsync(Path.Combine(_gitFixture.RootDirectory, TargetName, "marker.txt"), "idle repository");
    }

    [When(@"an operator deletes that repository")]
    public async Task WhenAnOperatorDeletesThatRepository() => _deleteResult = await RepositoryManager.DeleteAsync(TargetName, CancellationToken.None);

    [Then(@"the repository no longer exists on disk or in the repository list")]
    public async Task ThenTheRepositoryNoLongerExistsOnDiskOrInTheRepositoryList()
    {
        Assert.False(Directory.Exists(Path.Combine(_gitFixture!.RootDirectory, TargetName)));

        var summaries = await RepositoryManager.ListAsync(CancellationToken.None);
        Assert.DoesNotContain(summaries, r => r.Name == TargetName);
    }

    [Then(@"a history record exists for the deletion with a completed outcome")]
    public async Task ThenAHistoryRecordExistsForTheDeletionWithACompletedOutcome()
    {
        var accepted = Assert.IsType<RepositoryActionResult.Accepted>(_deleteResult);
        var run = await RunRepository.FindByIdAsync(accepted.RunId, CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(RunKind.RepositoryDelete, run.Kind);
        Assert.Equal(RunOutcome.Completed, run.Outcome);
    }

    [Given(@"a file inside that repository is locked open")]
    public void GivenAFileInsideThatRepositoryIsLockedOpen()
    {
        var markerPath = Path.Combine(_gitFixture!.RootDirectory, TargetName, "marker.txt");

        // FileShare.Read (no Delete flag) - matches how a real locking process would hold a file
        // open (e.g. still loaded, or mid-scan) - Directory.Delete's own attempt to remove this
        // file then fails with the classic "being used by another process" IOException, the exact
        // real-machine scenario that surfaced RepositoryManager.DeleteAsync's missing catch.
        _lockedFile = new FileStream(markerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    [Then(@"the deletion is accepted but the run records a failed outcome with error detail")]
    public async Task ThenTheDeletionIsAcceptedButTheRunRecordsAFailedOutcome()
    {
        var accepted = Assert.IsType<RepositoryActionResult.Accepted>(_deleteResult);
        var run = await RunRepository.FindByIdAsync(accepted.RunId, CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(RunOutcome.Failed, run.Outcome);

        // The exact bug this scenario guards against: CompletedAt getting set by the method's own
        // `finally` block while Outcome silently stayed at Running, because nothing caught the
        // exception Directory.Delete threw - a contradictory persisted state (in flight, but with
        // a completion timestamp) that's now impossible since the catch fixes the exact cause.
        Assert.NotNull(run.CompletedAt);

        // The underlying exception's own message, not just a bare "it failed" - what makes the
        // failure diagnosable from the dashboard instead of requiring host-side log access.
        Assert.False(string.IsNullOrEmpty(run.Stderr));
    }

    [Given(@"a file inside that repository is read-only")]
    public void GivenAFileInsideThatRepositoryIsReadOnly()
    {
        var markerPath = Path.Combine(_gitFixture!.RootDirectory, TargetName, "marker.txt");

        // Matches a real git checkout, where git itself sometimes leaves pack/object files
        // read-only - Directory.Delete(recursive: true) throws UnauthorizedAccessException for a
        // read-only file instead of just removing it, the exact real-machine bug this scenario
        // guards against (deletion silently kept failing even though nothing held the file open).
        File.SetAttributes(markerPath, File.GetAttributes(markerPath) | FileAttributes.ReadOnly);
    }

    [Then(@"the repository still exists on disk")]
    public void ThenTheRepositoryStillExistsOnDisk() =>
        Assert.True(Directory.Exists(Path.Combine(_gitFixture!.RootDirectory, TargetName)));

    [When(@"an operator attempts to delete a name that does not exist")]
    public async Task WhenAnOperatorAttemptsToDeleteANameThatDoesNotExist()
    {
        await EnsureFactoryAsync();
        _deleteResult = await RepositoryManager.DeleteAsync("does-not-exist", CancellationToken.None);
    }

    [Then(@"the deletion is rejected as not found")]
    public void ThenTheDeletionIsRejectedAsNotFound()
    {
        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(_deleteResult);
        Assert.Equal(RepositoryActionRejectionReason.NotFound, rejected.Reason);
    }

    [Given(@"a command run is in flight against a repository")]
    public async Task GivenACommandRunIsInFlightAgainstARepository()
    {
        await EnsureFactoryAsync();

        // Deterministic, same reasoning as "a clone into a given name is already in flight" - a
        // real `git`/`dotnet` command against a tiny fixture repository finishes near-instantly, not
        // reliably still in flight by the time the delete attempt below runs. RunRegistry doesn't
        // care which kind holds a lock, only that something does, so this exercises the exact same
        // conflict path a real in-flight command would hit.
        _inFlightTargetPath = Path.Combine(_gitFixture!.RootDirectory, "GitFixture");
        _inFlightRunId = Guid.NewGuid();
        _inFlightCts = new CancellationTokenSource();
        RunRegistry.TryAcquire(_inFlightTargetPath, _inFlightRunId, _inFlightCts, out _);

        await RunRepository.AddAsync(new Run
        {
            Id = _inFlightRunId,
            Kind = RunKind.GitCommand,
            RepositoryPath = _inFlightTargetPath,
            Arguments = ["status"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);
    }

    [When(@"an operator attempts to delete that repository")]
    public async Task WhenAnOperatorAttemptsToDeleteThatRepository() => _deleteResult = await RepositoryManager.DeleteAsync("GitFixture", CancellationToken.None);

    [Then(@"the deletion is rejected as a conflict identifying the in-flight run")]
    public void ThenTheDeletionIsRejectedAsAConflictIdentifyingTheInFlightRun()
    {
        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(_deleteResult);
        Assert.Equal(_inFlightRunId, rejected.ConflictingRunId);
    }

    [Then(@"the repository's files are untouched")]
    public void ThenTheRepositorySFilesAreUntouched()
    {
        Assert.True(Directory.Exists(Path.Combine(_gitFixture!.RootDirectory, "GitFixture")));
        Assert.True(Directory.Exists(Path.Combine(_gitFixture.RootDirectory, "GitFixture", ".git")));
    }

    [Then(@"the in-flight run is still running")]
    public async Task ThenTheInFlightRunIsStillRunning()
    {
        var run = await RunRepository.FindByIdAsync(_inFlightRunId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal(RunOutcome.Running, run.Outcome);
        Assert.True(RunRegistry.IsHeld(_inFlightTargetPath));
    }

    [When(@"a bearer-token caller sends a cancellation request naming the clone's run id")]
    public async Task WhenABearerTokenCallerSendsACancellationRequestNamingTheCloneSRunId()
    {
        using var client = _factory!.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/run/{_inFlightRunId}/cancel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _httpResponse = await client.SendAsync(request, CancellationToken.None);
    }

    [Then(@"the system responds as if the run id were unknown")]
    public void ThenTheSystemRespondsAsIfTheRunIdWereUnknown() => Assert.Equal(HttpStatusCode.NotFound, _httpResponse!.StatusCode);

    private bool _cancelAccepted;

    [When(@"an operator cancels that clone from the dashboard")]
    public void WhenAnOperatorCancelsThatCloneFromTheDashboard() =>
        // The same in-process call a Blazor "Cancel clone" button makes - RunRegistry.TryCancel
        // directly, never the REST cancel endpoint (per specs/repository-management). This
        // scenario's Given pre-acquires the lock directly (see the note there) rather than driving
        // a real, controllably-slow clone process, so what's verifiable here is the cancellation
        // signal itself being accepted for an in-flight repository-management run id - the same
        // contract RunEndpoints' cancel handler already relies on for dotnet/git runs (§8/§9).
        _cancelAccepted = RunRegistry.TryCancel(_inFlightRunId);

    [Then(@"the cancellation is accepted")]
    public void ThenTheCancellationIsAccepted() => Assert.True(_cancelAccepted);

    private async Task WaitForTerminalCloneAsync(string name)
    {
        var targetPath = Path.Combine(_gitFixture!.RootDirectory, name);
        for (var i = 0; i < 100; i++)
        {
            if (!RunRegistry.IsHeld(targetPath))
            {
                return;
            }

            await Task.Delay(50, CancellationToken.None);
        }

        throw new TimeoutException($"Clone into '{name}' did not reach a terminal state in time.");
    }

    private IReadOnlyList<SseEvent> _events = [];

    /// <inheritdoc />
    public void Dispose()
    {
        _httpResponse?.Dispose();
        _lockedFile?.Dispose();
        _inFlightCts?.Dispose();
        _scope?.Dispose();
        _factory?.Dispose();
        _gitFixture?.Dispose();
    }
}
