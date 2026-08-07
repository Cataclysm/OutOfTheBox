// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>DotnetCommandExecution.feature</c>.</summary>
[Binding]
public sealed class DotnetCommandExecutionSteps : IDisposable
{
    private CommandExecutionServiceFactory? _factory;
    private int _defaultTimeoutSecondsOverride = 600;
    private HttpResponseMessage? _response;
    private IReadOnlyList<SseEvent> _events = [];

    [Given(@"the configured default execution timeout is (\d+) seconds")]
    public void GivenTheConfiguredDefaultExecutionTimeoutIsSeconds(int seconds)
    {
        _defaultTimeoutSecondsOverride = seconds;
    }

    [When(@"an authenticated caller starts ""(.*)"" against ""(.*)""")]
    public Task WhenAnAuthenticatedCallerStarts(string subcommand, string fixtureName) =>
        StartRunAsync(new { arguments = new[] { subcommand }, workingDirectory = fixtureName }, streaming: true);

    [When(@"a non-streaming authenticated caller starts ""(.*)"" against ""(.*)""")]
    public Task WhenANonStreamingAuthenticatedCallerStarts(string subcommand, string fixtureName) =>
        StartRunAsync(new { arguments = new[] { subcommand }, workingDirectory = fixtureName }, streaming: false);

    [When(@"an authenticated caller starts a run with no timeout against ""(.*)""")]
    public Task WhenAnAuthenticatedCallerStartsARunWithNoTimeoutAgainst(string fixtureName) =>
        StartRunAsync(new { arguments = new[] { "test" }, workingDirectory = fixtureName }, streaming: true);

    [When(@"an authenticated caller starts a run with a (\d+) second timeout against ""(.*)""")]
    public Task WhenAnAuthenticatedCallerStartsARunWithATimeoutAgainst(int timeoutSeconds, string fixtureName) =>
        StartRunAsync(new { arguments = new[] { "test" }, workingDirectory = fixtureName, timeoutSeconds }, streaming: true);

    private async Task StartRunAsync(object body, bool streaming)
    {
        _factory = new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: _defaultTimeoutSecondsOverride);
        using var client = _factory.CreateClient();

        var result = await SseTestClient.PostAndReadAllEventsAsync(
            client,
            "/run",
            body,
            CommandExecutionServiceFactory.TestBearerToken,
            streaming,
            CancellationToken.None);

        _response = result.Response;
        _events = result.Events;
    }

    [Then(@"a run id is returned")]
    public void ThenARunIdIsReturned()
    {
        Assert.True(_response!.Headers.Contains("X-Run-Id"));
    }

    [Then(@"the run completes with exit code (\d+)")]
    public void ThenTheRunCompletesWithExitCode(int expectedExitCode)
    {
        var doneEvent = Assert.Single(_events, e => e.Name == "done");
        using var payload = JsonDocument.Parse(doneEvent.Data);
        Assert.Equal(expectedExitCode, payload.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Then(@"the run completes with a non-zero exit code")]
    public void ThenTheRunCompletesWithANonZeroExitCode()
    {
        var doneEvent = Assert.Single(_events, e => e.Name == "done");
        using var payload = JsonDocument.Parse(doneEvent.Data);
        Assert.NotEqual(0, payload.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Then(@"output events were received before completion")]
    public void ThenOutputEventsWereReceivedBeforeCompletion()
    {
        var eventList = _events.ToList();
        var doneIndex = eventList.FindIndex(e => e.Name == "done");
        var firstOutputIndex = eventList.FindIndex(e => e.Name is "stdout" or "stderr");

        Assert.True(firstOutputIndex >= 0, "Expected at least one stdout/stderr event.");
        Assert.True(firstOutputIndex < doneIndex, "Expected output events to precede the done event.");
    }

    [Then(@"the run is killed with reason ""(.*)""")]
    public void ThenTheRunIsKilledWithReason(string expectedReason)
    {
        var errorEvent = Assert.Single(_events, e => e.Name == "error");
        using var payload = JsonDocument.Parse(errorEvent.Data);
        Assert.Equal(expectedReason, payload.RootElement.GetProperty("reason").GetString());
    }

    public void Dispose()
    {
        _response?.Dispose();
        _factory?.Dispose();
    }
}
