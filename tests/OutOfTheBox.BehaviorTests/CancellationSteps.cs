// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>Cancellation.feature</c>.</summary>
[Binding]
public sealed class CancellationSteps : IDisposable
{
    private CommandExecutionServiceFactory? _factory;
    private HttpClient? _client;
    private HttpResponseMessage? _inFlightResponse;
    private Guid _runId;
    private HttpResponseMessage? _cancelResponse;
    private Task<SseRunResult>? _secondRunTask;

    // Only populated by the git-specific scenario below - see GitFixture.cs and
    // ConcurrencyAndLockingSteps for why a git-backed in-flight run needs its own factory/client
    // and can't rely on its own response headers to discover its run id.
    private GitFixture? _gitFixture;
    private CommandExecutionServiceFactory? _gitFactory;
    private HttpClient? _gitClient;

    private CommandExecutionServiceFactory Factory => _factory ??= new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: 30);

    private HttpClient Client => _client ??= Factory.CreateClient();

    [Given(@"a cancellable in-flight run against ""(.*)""")]
    public async Task GivenACancellableInFlightRunAgainst(string fixtureName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/run")
        {
            Content = JsonContent.Create(new { arguments = new[] { "test" }, workingDirectory = fixtureName }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _inFlightResponse = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
        _runId = Guid.Parse(_inFlightResponse.Headers.GetValues("X-Run-Id").Single());
    }

    [Given(@"a run has already completed against ""(.*)""")]
    public async Task GivenARunHasAlreadyCompletedAgainst(string fixtureName)
    {
        var result = await SseTestClient.PostAndReadAllEventsAsync(
            Client,
            "/run",
            new { arguments = new[] { "test" }, workingDirectory = fixtureName },
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);

        _runId = Guid.Parse(result.Response.Headers.GetValues("X-Run-Id").Single());
        result.Response.Dispose();
    }

    [Given(@"a cancellable in-flight git run against the git fixture")]
    public async Task GivenACancellableInFlightGitRunAgainstTheGitFixture()
    {
        _gitFixture = await GitFixture.CreateAsync(withBlockingHook: true);
        _gitFactory = new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: 30, rootDirectoryOverride: _gitFixture.RootDirectory);
        _gitClient = _gitFactory.CreateClient();

        // Fire-and-forget, not awaited - see ConcurrencyAndLockingSteps for why this specific
        // combination (a genuinely-blocked native process tree behind /run/git) can't be relied
        // on to deliver its own response promptly. The run id is instead discovered below via the
        // conflict payload of a throwaway probe request, once the lock is confirmed held.
        var request = new HttpRequestMessage(HttpMethod.Post, "/run/git")
        {
            Content = JsonContent.Create(new { arguments = new[] { "commit", "--allow-empty", "-m", "blocked" }, workingDirectory = "GitFixture" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);
        _inFlightResponse = null;
        _ = _gitClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        // Give the fire-and-forget send an initial scheduling opportunity before the first poll -
        // reduces (does not fully eliminate) an observed occasional delay before the background
        // request's handler actually starts running.
        await Task.Delay(50);

        using var pollingClient = _gitFactory.CreateClient();
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var probe = await SseTestClient.PostAndReadAllEventsAsync(
                pollingClient,
                "/run",
                new { arguments = new[] { "--version" }, workingDirectory = "GitFixture" },
                CommandExecutionServiceFactory.TestBearerToken,
                streaming: true,
                CancellationToken.None);

            var rejection = probe.Events.FirstOrDefault(e => e.Name == "error");
            probe.Response.Dispose();

            if (rejection is not null)
            {
                using var payload = JsonDocument.Parse(rejection.Data);
                if (payload.RootElement.TryGetProperty("runId", out var runIdProperty))
                {
                    _runId = runIdProperty.GetGuid();
                    return;
                }
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("The git run's repo lock was never observed as held.");
    }

    [When(@"that run is cancelled")]
    [When(@"that run is cancelled again")]
    public Task WhenThatRunIsCancelled() => CancelAsync(Client, _runId);

    [When(@"that git run is cancelled")]
    public Task WhenThatGitRunIsCancelled() =>
        // A fresh client, not _gitClient (which still has the original blocked commit request
        // pending on it) - see the head-of-line-blocking note in ConcurrencyAndLockingSteps.
        CancelAsync(_gitFactory!.CreateClient(), _runId);

    [When(@"a cancel request is sent for an unknown run id")]
    public Task WhenACancelRequestIsSentForAnUnknownRunId() => CancelAsync(Client, Guid.NewGuid());

    private async Task CancelAsync(HttpClient client, Guid runId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/run/{runId}/cancel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _cancelResponse?.Dispose();
        _cancelResponse = await client.SendAsync(request, CancellationToken.None);
    }

    [Then(@"the cancel request is accepted")]
    public void ThenTheCancelRequestIsAccepted() => Assert.Equal(HttpStatusCode.Accepted, _cancelResponse!.StatusCode);

    [Then(@"the cancel request is rejected as not found")]
    public void ThenTheCancelRequestIsRejectedAsNotFound() => Assert.Equal(HttpStatusCode.NotFound, _cancelResponse!.StatusCode);

    [Then(@"the run's stream ends with reason ""(.*)""")]
    public async Task ThenTheRunSStreamEndsWithReason(string expectedReason)
    {
        await using var stream = await _inFlightResponse!.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? currentEventName = null;
        string? lastErrorData = null;
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && currentEventName == "error")
            {
                lastErrorData = line["data: ".Length..];
            }
        }

        Assert.NotNull(lastErrorData);
        using var payload = JsonDocument.Parse(lastErrorData);
        Assert.Equal(expectedReason, payload.RootElement.GetProperty("reason").GetString());
    }

    [Then(@"a subsequent run against ""(.*)"" is accepted")]
    public async Task ThenASubsequentRunAgainstIsAccepted(string fixtureName)
    {
        _secondRunTask = SseTestClient.PostAndReadAllEventsAsync(
            Client,
            "/run",
            new { arguments = new[] { "test" }, workingDirectory = fixtureName, timeoutSeconds = 5 },
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);

        var result = await _secondRunTask;

        var conflictRejection = result.Events
            .Where(e => e.Name == "error")
            .Select(e => JsonDocument.Parse(e.Data))
            .Any(payload => payload.RootElement.TryGetProperty("runId", out _));

        Assert.False(conflictRejection, "Expected the repo to be free after cancellation, not still locked.");
    }

    [Then(@"the git fixture repo is accepted for a subsequent run")]
    public async Task ThenTheGitFixtureRepoIsAcceptedForASubsequentRun()
    {
        using var pollingClient = _gitFactory!.CreateClient();

        // Cancel returning 202 means cancellation was *requested*, not that the process tree has
        // actually finished terminating and RunEndpoints' finally block has released the lock yet
        // (killing a native git.exe -> sh.exe -> ping.exe tree takes measurably longer than the
        // managed HangingFixture case the other cancellation scenarios use) - so this polls rather
        // than asserting on a single attempt.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            _secondRunTask = SseTestClient.PostAndReadAllEventsAsync(
                pollingClient,
                "/run",
                new { arguments = new[] { "--version" }, workingDirectory = "GitFixture" },
                CommandExecutionServiceFactory.TestBearerToken,
                streaming: true,
                CancellationToken.None);

            var result = await _secondRunTask;

            var conflictRejection = result.Events
                .Where(e => e.Name == "error")
                .Select(e => JsonDocument.Parse(e.Data))
                .Any(payload => payload.RootElement.TryGetProperty("runId", out _));

            if (!conflictRejection)
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("Expected the repo to become free after cancellation, not remain locked.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _inFlightResponse?.Dispose();
        _cancelResponse?.Dispose();

        if (_secondRunTask is { IsCompletedSuccessfully: true } task)
        {
            task.Result.Response.Dispose();
        }

        _client?.Dispose();
        _factory?.Dispose();
        _gitClient?.Dispose();
        _gitFactory?.Dispose();
        _gitFixture?.Dispose();
    }
}
