// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.BehaviorTests.Support;
using OutOfTheBox.Domain.Repositories;
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
    private McpToolCallResult? _gitRunToolCallResult;
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
        _cloneResult = await RepositoryManager.CloneAsync(SourceRepositoryPath, TargetName, null, CancellationToken.None);

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
    public async Task WhenAnOperatorAttemptsToCloneIntoThatSameName() => _cloneResult = await RepositoryManager.CloneAsync(SourceRepositoryPath, TargetName, null, CancellationToken.None);

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
    public async Task WhenASecondCloneIntoThatSameNameIsRequestedBeforeTheFirstFinishes() => _cloneResult = await RepositoryManager.CloneAsync(SourceRepositoryPath, TargetName, null, CancellationToken.None);

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
        _gitRunToolCallResult = await McpTestClient.CallToolAsync(
            client, "git_run", new { arguments = new[] { "status" }, workingDirectory = TargetName }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
    }

    [Then(@"the command is rejected as a repository conflict")]
    public void ThenTheCommandIsRejectedAsARepoConflict()
    {
        Assert.True(_gitRunToolCallResult!.IsToolError, "Expected git_run to be rejected as a repository conflict.");
        Assert.Contains(_inFlightRunId.ToString(), _gitRunToolCallResult.ContentText);
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


    private string? _diffResult;

    [When(@"an operator requests the diff for ""(.*)"" in the fixture repository's initial commit")]
    public async Task WhenAnOperatorRequestsTheDiffForInTheFixtureRepositorySInitialCommit(string relativePath)
    {
        await EnsureFactoryAsync();

        var commits = await RepositoryManager.ListCommitsAsync("GitFixture", 0, 1, CancellationToken.None);
        var initialCommitHash = Assert.Single(commits).Hash;

        _diffResult = await RepositoryManager.GetCommitFileDiffAsync("GitFixture", initialCommitHash, relativePath, CancellationToken.None);
    }

    [Then(@"the diff shows ""(.*)"" as the changed file with an added ""(.*)"" line")]
    public void ThenTheDiffShowsAsTheChangedFileWithAnAddedLine(string path, string addedLine)
    {
        Assert.NotNull(_diffResult);
        Assert.Contains(path, _diffResult);
        Assert.Contains($"+{addedLine}", _diffResult);
    }

    [Then(@"no diff is returned")]
    public void ThenNoDiffIsReturned() => Assert.Null(_diffResult);

    private CommitDetail? _commitDetail;

    [When(@"an operator requests the commit detail for the fixture repository's initial commit")]
    public async Task WhenAnOperatorRequestsTheCommitDetailForTheFixtureRepositorySInitialCommit()
    {
        await EnsureFactoryAsync();

        var commits = await RepositoryManager.ListCommitsAsync("GitFixture", 0, 1, CancellationToken.None);
        var initialCommitHash = Assert.Single(commits).Hash;

        _commitDetail = await RepositoryManager.GetCommitDetailAsync("GitFixture", initialCommitHash, CancellationToken.None);
    }

    [Then(@"the changed file ""(.*)"" shows (\d+) lines? added and (\d+) lines? removed")]
    public void ThenTheChangedFileShowsLinesAddedAndLinesRemoved(string path, int added, int removed)
    {
        Assert.NotNull(_commitDetail);
        var file = Assert.Single(_commitDetail.Files, f => f.Path == path);
        Assert.Equal(added, file.LinesAdded);
        Assert.Equal(removed, file.LinesRemoved);
    }

    [Given(@"an untracked file exists in the fixture repository's working tree")]
    public async Task GivenAnUntrackedFileExistsInTheFixtureRepositorySWorkingTree()
    {
        await EnsureFactoryAsync();
        await File.WriteAllTextAsync(Path.Combine(SourceRepositoryPath, "untracked.txt"), "dirty");
    }

    private IReadOnlyList<string>? _dirtyFilePaths;

    [When(@"an operator lists the fixture repository's dirty file paths")]
    public async Task WhenAnOperatorListsTheFixtureRepositorySDirtyFilePaths()
    {
        await EnsureFactoryAsync();
        _dirtyFilePaths = await RepositoryManager.ListDirtyFilePathsAsync("GitFixture", CancellationToken.None);
    }

    [Then(@"the dirty file paths include ""(.*)""")]
    public void ThenTheDirtyFilePathsInclude(string path)
    {
        Assert.NotNull(_dirtyFilePaths);
        Assert.Contains(path, _dirtyFilePaths);
    }

    [Then(@"the dirty file paths are empty")]
    public void ThenTheDirtyFilePathsAreEmpty()
    {
        Assert.NotNull(_dirtyFilePaths);
        Assert.Empty(_dirtyFilePaths);
    }

    [Given(@"a second commit exists on top of the fixture repository's initial commit")]
    public async Task GivenASecondCommitExistsOnTopOfTheFixtureRepositorySInitialCommit()
    {
        await EnsureFactoryAsync();
        await File.WriteAllTextAsync(Path.Combine(SourceRepositoryPath, "second.txt"), "second commit content");
        await GitFixture.RunGitAsync(SourceRepositoryPath, "add", "-A");
        await GitFixture.RunGitAsync(SourceRepositoryPath, "commit", "-q", "-m", "second commit");
    }

    [When(@"an operator requests the commit detail for the fixture repository's newest commit")]
    public async Task WhenAnOperatorRequestsTheCommitDetailForTheFixtureRepositorySNewestCommit()
    {
        await EnsureFactoryAsync();

        var commits = await RepositoryManager.ListCommitsAsync("GitFixture", 0, 1, CancellationToken.None);
        var newestCommitHash = Assert.Single(commits).Hash;

        _commitDetail = await RepositoryManager.GetCommitDetailAsync("GitFixture", newestCommitHash, CancellationToken.None);
    }

    [Then(@"its single parent shows the initial commit's subject")]
    public void ThenItsSingleParentShowsTheInitialCommitsSubject()
    {
        Assert.NotNull(_commitDetail);
        var parent = Assert.Single(_commitDetail.Parents);
        Assert.Equal("initial commit", parent.Subject);
    }

    private bool _cancelAccepted;

    [When(@"an operator cancels that clone from the dashboard")]
    public void WhenAnOperatorCancelsThatCloneFromTheDashboard() =>
        // The same in-process call a Blazor "Cancel clone" button makes - RunRegistry.TryCancel
        // directly (per specs/repository-management). This scenario's Given pre-acquires the lock
        // directly (see the note there) rather than driving a real, controllably-slow clone process,
        // so what's verifiable here is the cancellation signal itself being accepted for an
        // in-flight repository-management run id - the same contract the cancel_run MCP tool relies
        // on for dotnet/git runs (per mcp-command-execution).
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

    /// <inheritdoc />
    public void Dispose()
    {
        _gitRunToolCallResult?.Response.Dispose();
        _lockedFile?.Dispose();
        _inFlightCts?.Dispose();
        _scope?.Dispose();
        _factory?.Dispose();
        _gitFixture?.Dispose();
    }
}
