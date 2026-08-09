// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpCommandExecution.feature</c>.</summary>
[Binding]
public sealed class McpCommandExecutionSteps : IDisposable
{
    private readonly CommandExecutionServiceFactory _factory = new(defaultExecutionTimeoutSeconds: 30);
    private HttpClient? _client;

    // Only populated by the git_run scenario, which needs a GitFixture-rooted service instance.
    private GitFixture? _gitFixture;
    private CommandExecutionServiceFactory? _gitFactory;
    private HttpClient? _gitClient;

    private McpToolCallResult? _toolCallResult;
    private Guid _runId;
    private long _lastOffset;
    private JsonElement _lastReadResult;
    private HttpClient? _restProbeClient;
    private HttpResponseMessage? _restInFlightResponse;
    private SseRunResult? _restSecondRunResult;

    private HttpClient Client => _client ??= _factory.CreateClient();

    [When(@"an authenticated caller starts a dotnet_run ""(.*)"" against ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerStartsADotnetRunAgainst(string subcommand, string fixtureName) =>
        await StartRunAsync(Client, "dotnet_run", subcommand, fixtureName, timeoutSeconds: null);

    [When(@"an authenticated caller starts a dotnet_run against ""(.*)"" with a (\d+) second timeout")]
    public async Task WhenAnAuthenticatedCallerStartsADotnetRunAgainstWithATimeout(string fixtureName, int timeoutSeconds) =>
        await StartRunAsync(Client, "dotnet_run", "test", fixtureName, timeoutSeconds);

    [When(@"an authenticated caller starts a git_run ""(.*)"" against the git fixture")]
    public async Task WhenAnAuthenticatedCallerStartsAGitRunAgainstTheGitFixture(string subcommand)
    {
        _gitFixture = await GitFixture.CreateAsync();
        _gitFactory = new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: 30, rootDirectoryOverride: _gitFixture.RootDirectory);
        _gitClient = _gitFactory.CreateClient();

        _toolCallResult = await McpTestClient.CallToolAsync(
            _gitClient,
            "git_run",
            new { arguments = new[] { subcommand }, workingDirectory = "GitFixture" },
            CommandExecutionServiceFactory.TestBearerToken,
            CancellationToken.None);

        _runId = ExtractRunId(_toolCallResult);
    }

    [Given(@"an in-flight dotnet_run against ""(.*)"" with a (\d+) second timeout")]
    public async Task GivenAnInFlightDotnetRunAgainstWithATimeout(string fixtureName, int timeoutSeconds) =>
        await StartRunAsync(Client, "dotnet_run", "test", fixtureName, timeoutSeconds);

    [Given(@"a dotnet_run against ""(.*)"" has already completed")]
    public async Task GivenADotnetRunAgainstHasAlreadyCompleted(string fixtureName)
    {
        await StartRunAsync(Client, "dotnet_run", "test", fixtureName, timeoutSeconds: null);
        await PollUntilTerminalAsync(Client, _runId, TimeSpan.FromSeconds(30));
    }

    [Then(@"an MCP run id is returned")]
    public void ThenAnMcpRunIdIsReturned() => Assert.NotEqual(Guid.Empty, _runId);

    [Then(@"read_run_output eventually reports status ""(.*)"" with exit code (\d+)")]
    public async Task ThenReadRunOutputEventuallyReportsStatusWithExitCode(string status, int exitCode)
    {
        var result = await PollUntilTerminalAsync(GitClientOrDefault, _runId, TimeSpan.FromSeconds(30));
        Assert.Equal(status, result.GetProperty("status").GetString());
        Assert.Equal(exitCode, result.GetProperty("exitCode").GetInt32());
    }

    [Then(@"read_run_output eventually reports a non-zero exit code")]
    public async Task ThenReadRunOutputEventuallyReportsANonZeroExitCode()
    {
        var result = await PollUntilTerminalAsync(Client, _runId, TimeSpan.FromSeconds(30));
        Assert.NotEqual(0, result.GetProperty("exitCode").GetInt32());
    }

    [Then(@"read_run_output eventually reports status ""(.*)""")]
    public async Task ThenReadRunOutputEventuallyReportsStatus(string status)
    {
        var result = await PollUntilStatusAsync(Client, _runId, status, TimeSpan.FromSeconds(30));
        Assert.Equal(status, result.GetProperty("status").GetString());
    }

    [When(@"read_run_output is called once it reaches a terminal state")]
    public async Task WhenReadRunOutputIsCalledOnceItReachesATerminalState()
    {
        _lastReadResult = await PollUntilTerminalAsync(Client, _runId, TimeSpan.FromSeconds(30));
        _lastOffset = _lastReadResult.GetProperty("nextOffset").GetInt64();
    }

    [When(@"read_run_output is called again with the offset from the previous call")]
    public async Task WhenReadRunOutputIsCalledAgainWithTheOffsetFromThePreviousCall()
    {
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "read_run_output", new { runId = _runId, offset = _lastOffset }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        _lastReadResult = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;
    }

    [Then(@"the second read_run_output call returns no additional output")]
    public void ThenTheSecondReadRunOutputCallReturnsNoAdditionalOutput()
    {
        Assert.Equal(string.Empty, _lastReadResult.GetProperty("stdout").GetString());
        Assert.Equal(string.Empty, _lastReadResult.GetProperty("stderr").GetString());
    }

    [When(@"an authenticated caller calls read_run_output with an unknown run id")]
    public async Task WhenAnAuthenticatedCallerCallsReadRunOutputWithAnUnknownRunId() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "read_run_output", new { runId = Guid.NewGuid(), offset = 0 }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [When(@"the caller calls cancel_run for that run")]
    public async Task WhenTheCallerCallsCancelRunForThatRun() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "cancel_run", new { runId = _runId }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [When(@"the caller calls cancel_run for an unknown run id")]
    public async Task WhenTheCallerCallsCancelRunForAnUnknownRunId() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "cancel_run", new { runId = Guid.NewGuid() }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"cancel_run returns the run's existing status without error")]
    public void ThenCancelRunReturnsTheRunSExistingStatusWithoutError()
    {
        Assert.False(_toolCallResult!.IsToolError);
        Assert.Null(_toolCallResult.JsonRpcError);
        var result = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;
        Assert.Equal("completed", result.GetProperty("status").GetString());
    }

    [Then(@"the MCP call is rejected")]
    public void ThenTheMcpCallIsRejected() =>
        Assert.True(_toolCallResult!.JsonRpcError is not null || _toolCallResult.IsToolError, "Expected the MCP call to be rejected.");

    [Given(@"a REST run is in flight against ""(.*)""")]
    public async Task GivenARestRunIsInFlightAgainst(string fixtureName)
    {
        // A dedicated HttpClient, not the shared one the subsequent MCP tool call uses - the same
        // fix ConcurrencyAndLockingSteps' own remarks already document for the equivalent problem:
        // this response's body is a still-open, never-fully-drained SSE stream (HangingFixture never
        // completes on its own), and sharing one HttpClient meant a later request on it blocked
        // behind this still-open one instead of getting its own.
        _restProbeClient = _factory.CreateClient();

        // A short caller-supplied timeout, not the factory's 30s default - this run is deliberately
        // never explicitly cancelled by this scenario (only its rejection of a second, conflicting
        // request is under test), and its child process otherwise outlives the scenario's own scope
        // (nothing tears it down when the WebApplicationFactory disposes, since it's detached from
        // any HTTP request/response lifecycle by then) - a short bound here keeps that orphaned
        // process, and the delay it causes the test host to actually exit, short too.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/run")
        {
            Content = JsonContent.Create(new { arguments = new[] { "test" }, workingDirectory = fixtureName, timeoutSeconds = 3 }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _restInFlightResponse = await _restProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
    }

    [When(@"a REST run is started against ""(.*)""")]
    public async Task WhenARestRunIsStartedAgainst(string fixtureName) =>
        _restSecondRunResult = await SseTestClient.PostAndReadAllEventsAsync(
            Client,
            "/run",
            new { arguments = new[] { "--version" }, workingDirectory = fixtureName },
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);

    [Then(@"the REST run is rejected as a repository conflict")]
    public void ThenTheRestRunIsRejectedAsARepositoryConflict()
    {
        var rejected = _restSecondRunResult!.Events
            .Where(e => e.Name == "error")
            .Select(e => JsonDocument.Parse(e.Data))
            .Any(payload => payload.RootElement.TryGetProperty("runId", out _));

        Assert.True(rejected, "Expected the REST run to be rejected as a repository conflict.");
    }

    private HttpClient GitClientOrDefault => _gitClient ?? Client;

    private async Task StartRunAsync(HttpClient client, string toolName, string subcommand, string fixtureName, int? timeoutSeconds)
    {
        object arguments = timeoutSeconds is int seconds
            ? new { arguments = new[] { subcommand }, workingDirectory = fixtureName, timeoutSeconds = seconds }
            : new { arguments = new[] { subcommand }, workingDirectory = fixtureName };

        _toolCallResult = await McpTestClient.CallToolAsync(client, toolName, arguments, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        _runId = ExtractRunId(_toolCallResult);
    }

    private static Guid ExtractRunId(McpToolCallResult result)
    {
        if (result.IsToolError || result.JsonRpcError is not null || result.ContentText is null)
        {
            return Guid.Empty;
        }

        return JsonDocument.Parse(result.ContentText).RootElement.GetProperty("runId").GetGuid();
    }

    private async Task<JsonElement> PollUntilTerminalAsync(HttpClient client, Guid runId, TimeSpan timeout) =>
        await PollAsync(client, runId, timeout, status => status != "running");

    private async Task<JsonElement> PollUntilStatusAsync(HttpClient client, Guid runId, string targetStatus, TimeSpan timeout) =>
        await PollAsync(client, runId, timeout, status => status == targetStatus);

    private async Task<JsonElement> PollAsync(HttpClient client, Guid runId, TimeSpan timeout, Func<string, bool> isDone)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            var result = await McpTestClient.CallToolAsync(
                client, "read_run_output", new { runId, offset = 0 }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

            Assert.False(result.IsToolError, $"read_run_output returned an error: {result.ContentText}");
            var payload = JsonDocument.Parse(result.ContentText!).RootElement;

            if (isDone(payload.GetProperty("status").GetString()!))
            {
                return payload;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Run {runId} did not reach the expected status within {timeout}.");
            }

            await Task.Delay(100);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _toolCallResult?.Response.Dispose();
        _restInFlightResponse?.Dispose();
        _restSecondRunResult?.Response.Dispose();
        _restProbeClient?.Dispose();
        _client?.Dispose();
        _factory.Dispose();
        _gitClient?.Dispose();
        _gitFactory?.Dispose();
        _gitFixture?.Dispose();
    }
}
