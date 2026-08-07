// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>ConcurrencyAndLocking.feature</c>.</summary>
[Binding]
public sealed class ConcurrencyAndLockingSteps : IDisposable
{
    private CommandExecutionServiceFactory? _factory;
    private HttpClient? _client;
    private HttpResponseMessage? _inFlightResponse;
    private Guid _inFlightRunId;
    private Task<SseRunResult>? _concurrentTaskA;
    private Task<SseRunResult>? _concurrentTaskB;
    private SseRunResult? _secondRunResult;

    // Only populated by the cross-kind scenarios below, which need a GitFixture-rooted service
    // instance (the default Factory/Client above stay pointed at the checked-in tests/Fixtures/).
    private GitFixture? _gitFixture;
    private CommandExecutionServiceFactory? _gitFactory;
    private HttpClient? _gitClient;

    private CommandExecutionServiceFactory Factory => _factory ??= new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: 30);

    private HttpClient Client => _client ??= Factory.CreateClient();

    [When(@"authenticated runs are started concurrently against ""(.*)"" and ""(.*)""")]
    public void WhenAuthenticatedRunsAreStartedConcurrentlyAgainst(string fixtureA, string fixtureB)
    {
        _concurrentTaskA = StartAndReadAsync(fixtureA);
        _concurrentTaskB = StartAndReadAsync(fixtureB);
    }

    [Then(@"both concurrent runs complete independently")]
    public async Task ThenBothConcurrentRunsCompleteIndependently()
    {
        var results = await Task.WhenAll(_concurrentTaskA!, _concurrentTaskB!);

        foreach (var result in results)
        {
            Assert.True(result.Response.IsSuccessStatusCode);
            Assert.Contains(result.Events, e => e.Name == "done");
        }
    }

    [Given(@"an in-flight run against ""(.*)""")]
    public Task GivenAnInFlightRunAgainst(string fixtureName) =>
        StartInFlightRunAsync(fixtureName, timeoutSeconds: null);

    [Given(@"an in-flight run against ""(.*)"" with a (\d+) second timeout")]
    public Task GivenAnInFlightRunAgainstWithATimeout(string fixtureName, int timeoutSeconds) =>
        StartInFlightRunAsync(fixtureName, timeoutSeconds);

    [Given(@"an in-flight git run against the git fixture")]
    public async Task GivenAnInFlightGitRunAgainstTheGitFixture()
    {
        _gitFixture = await GitFixture.CreateAsync(withBlockingHook: true);
        _gitFactory = new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: 30, rootDirectoryOverride: _gitFixture.RootDirectory);
        _gitClient = _gitFactory.CreateClient();

        // Deliberately fire-and-forget, not awaited even for headers: investigation found that
        // once a genuinely-blocked native process tree (git.exe -> sh.exe -> ...) is involved, this
        // HttpClient can take an unpredictable, occasionally multi-second time to observe the
        // response at all (not even headers, despite ResponseHeadersRead) - confirmed via
        // server-side instrumentation that RunRegistry.TryAcquire itself still succeeds within
        // single-digit milliseconds of the request arriving, so this is a client/transport
        // observation quirk (root cause not fully identified; suspected scheduling interaction
        // between the background send and the redirected-pipe I/O of a multi-level native process
        // tree), not a service-side delay. The "When" step below polls for the lock instead of
        // relying on this request's own headers or timing.
        // Not disposed here (deliberately outlives this method) - the send is still in flight
        // when this step returns, and disposing the request out from under it would race.
        var request = new HttpRequestMessage(HttpMethod.Post, "/run/git")
        {
            Content = JsonContent.Create(new { arguments = new[] { "commit", "--allow-empty", "-m", "blocked" }, workingDirectory = "GitFixture" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);
        _ = _gitClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        // Give the fire-and-forget send an initial scheduling opportunity before the first poll.
        await Task.Delay(50);
    }

    [When(@"that run reaches a terminal state")]
    public async Task WhenThatRunReachesATerminalState()
    {
        // Drain the in-flight response's stream to completion; its short configured timeout is
        // what ends it.
        await using var stream = await _inFlightResponse!.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is not null)
        {
        }
    }

    [When(@"a second authenticated run is started against ""(.*)""")]
    public Task WhenASecondAuthenticatedRunIsStartedAgainst(string fixtureName) =>
        StartSecondRunAsync(Client, "/run", ["test"], fixtureName, timeoutSeconds: null);

    [When(@"a second authenticated run is started against ""(.*)"" with a (\d+) second timeout")]
    public Task WhenASecondAuthenticatedRunIsStartedAgainstWithATimeout(string fixtureName, int timeoutSeconds) =>
        StartSecondRunAsync(Client, "/run", ["test"], fixtureName, timeoutSeconds);

    [When(@"a second authenticated git run is started against ""(.*)""")]
    public Task WhenASecondAuthenticatedGitRunIsStartedAgainst(string fixtureName) =>
        // No real git repo exists at this path (HangingFixture is a plain dotnet fixture) - that's
        // fine, since the point of this scenario is that the request never gets far enough to
        // invoke git.exe at all: it must be rejected by the repo lock first.
        StartSecondRunAsync(Client, "/run/git", ["status"], fixtureName, timeoutSeconds: null);

    [When(@"a second authenticated run is started against the git fixture")]
    public async Task WhenASecondAuthenticatedRunIsStartedAgainstTheGitFixture()
    {
        // Polls instead of relying on the in-flight git request's own headers (see the comment in
        // GivenAnInFlightGitRunAgainstTheGitFixture) - each attempt is itself a fast, throwaway
        // "dotnet --version" that either gets rejected (the git run's lock is held, as expected)
        // or succeeds (the lock wasn't acquired yet); retries a bounded number of times rather
        // than trusting a single fixed delay. Uses a dedicated HttpClient, not the one the
        // still-pending git request is on - sharing one client meant every poll attempt queued
        // behind that still-open connection instead of getting its own.
        using var pollingClient = _gitFactory!.CreateClient();
        for (var attempt = 0; attempt < 150; attempt++)
        {
            await StartSecondRunAsync(pollingClient, "/run", ["--version"], "GitFixture", timeoutSeconds: null);

            var rejected = _secondRunResult!.Events
                .Where(e => e.Name == "error")
                .Select(e => JsonDocument.Parse(e.Data))
                .Any(payload => payload.RootElement.TryGetProperty("runId", out _));

            if (rejected)
            {
                return;
            }

            await Task.Delay(100);
        }
    }

    private async Task StartSecondRunAsync(HttpClient client, string requestUri, string[] arguments, string fixtureName, int? timeoutSeconds)
    {
        object body = timeoutSeconds is int seconds
            ? new { arguments, workingDirectory = fixtureName, timeoutSeconds = seconds }
            : new { arguments, workingDirectory = fixtureName };

        _secondRunResult = await SseTestClient.PostAndReadAllEventsAsync(
            client,
            requestUri,
            body,
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);
    }

    [Then(@"the second run is rejected identifying the in-flight run's id")]
    public void ThenTheSecondRunIsRejectedIdentifyingTheInFlightRunSId()
    {
        var errorEvent = Assert.Single(_secondRunResult!.Events, e => e.Name == "error");
        using var payload = JsonDocument.Parse(errorEvent.Data);
        Assert.Equal("validation", payload.RootElement.GetProperty("reason").GetString());
        Assert.Equal(_inFlightRunId, payload.RootElement.GetProperty("runId").GetGuid());
    }

    [Then(@"the second run is rejected as a repo conflict")]
    public void ThenTheSecondRunIsRejectedAsARepoConflict()
    {
        var conflictRejection = _secondRunResult!.Events
            .Where(e => e.Name == "error")
            .Select(e => JsonDocument.Parse(e.Data))
            .Any(payload => payload.RootElement.TryGetProperty("runId", out _));

        Assert.True(conflictRejection, "Expected the second run to be rejected as a repo conflict.");
    }

    [Then(@"the second run is accepted")]
    public void ThenTheSecondRunIsAccepted()
    {
        // "Accepted" means the repo's lock was free - not that this second run necessarily
        // completes successfully. Against HangingFixture it will time out on its own short
        // timeout, same as the first run did; what must NOT happen is a busy-repo rejection
        // (an "error" event carrying a conflicting runId).
        var conflictRejection = _secondRunResult!.Events
            .Where(e => e.Name == "error")
            .Select(e => JsonDocument.Parse(e.Data))
            .Any(payload => payload.RootElement.TryGetProperty("runId", out _));

        Assert.False(conflictRejection, "Expected the second run not to be rejected as a repo conflict.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _inFlightResponse?.Dispose();
        _secondRunResult?.Response.Dispose();
        _client?.Dispose();
        _factory?.Dispose();
        _gitClient?.Dispose();
        _gitFactory?.Dispose();
        _gitFixture?.Dispose();
    }

    private async Task StartInFlightRunAsync(string fixtureName, int? timeoutSeconds)
    {
        object body = timeoutSeconds is int seconds
            ? new { arguments = new[] { "test" }, workingDirectory = fixtureName, timeoutSeconds = seconds }
            : new { arguments = new[] { "test" }, workingDirectory = fixtureName };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/run") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _inFlightResponse = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
        _inFlightRunId = Guid.Parse(_inFlightResponse.Headers.GetValues("X-Run-Id").Single());
    }

    private Task<SseRunResult> StartAndReadAsync(string fixtureName) =>
        SseTestClient.PostAndReadAllEventsAsync(
            Client,
            "/run",
            new { arguments = new[] { "test" }, workingDirectory = fixtureName },
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);
}
