// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildAndTestService.BehaviorTests.Support;
using Reqnroll;

namespace BuildAndTestService.BehaviorTests;

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
        StartSecondRunAsync(fixtureName, timeoutSeconds: null);

    [When(@"a second authenticated run is started against ""(.*)"" with a (\d+) second timeout")]
    public Task WhenASecondAuthenticatedRunIsStartedAgainstWithATimeout(string fixtureName, int timeoutSeconds) =>
        StartSecondRunAsync(fixtureName, timeoutSeconds);

    private async Task StartSecondRunAsync(string fixtureName, int? timeoutSeconds)
    {
        object body = timeoutSeconds is int seconds
            ? new { arguments = new[] { "test" }, workingDirectory = fixtureName, timeoutSeconds = seconds }
            : new { arguments = new[] { "test" }, workingDirectory = fixtureName };

        _secondRunResult = await SseTestClient.PostAndReadAllEventsAsync(
            Client,
            "/run",
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
