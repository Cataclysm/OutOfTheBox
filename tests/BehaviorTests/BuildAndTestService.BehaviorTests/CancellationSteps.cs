// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildAndTestService.BehaviorTests.Support;
using Reqnroll;

namespace BuildAndTestService.BehaviorTests;

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

    [When(@"that run is cancelled")]
    [When(@"that run is cancelled again")]
    public Task WhenThatRunIsCancelled() => CancelAsync(_runId);

    [When(@"a cancel request is sent for an unknown run id")]
    public Task WhenACancelRequestIsSentForAnUnknownRunId() => CancelAsync(Guid.NewGuid());

    private async Task CancelAsync(Guid runId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/run/{runId}/cancel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _cancelResponse?.Dispose();
        _cancelResponse = await Client.SendAsync(request, CancellationToken.None);
    }

    [Then(@"the cancel request is accepted")]
    public void ThenTheCancelRequestIsAccepted()
    {
        Assert.Equal(HttpStatusCode.Accepted, _cancelResponse!.StatusCode);
    }

    [Then(@"the cancel request is rejected as not found")]
    public void ThenTheCancelRequestIsRejectedAsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, _cancelResponse!.StatusCode);
    }

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
    }
}
