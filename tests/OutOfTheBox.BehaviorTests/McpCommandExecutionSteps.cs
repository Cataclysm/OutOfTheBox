// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.BehaviorTests.Support;
using Microsoft.Extensions.DependencyInjection;
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
    private Task<McpToolCallResult>? _concurrentTaskA;
    private Task<McpToolCallResult>? _concurrentTaskB;

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
        // Reuses an already-created fixture/factory/client if a prior Given step (e.g. disabling a
        // subcommand in MCP Settings) needed one to exist first, rather than always starting fresh -
        // the setting change has to land on the exact same service instance this call reaches.
        _gitFixture ??= await GitFixture.CreateAsync();
        _gitFactory ??= new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: 30, rootDirectoryOverride: _gitFixture.RootDirectory);
        _gitClient ??= _gitFactory.CreateClient();

        _toolCallResult = await McpTestClient.CallToolAsync(
            _gitClient,
            "git_run",
            new { arguments = new[] { subcommand }, workingDirectory = "GitFixture" },
            CommandExecutionServiceFactory.TestBearerToken,
            CancellationToken.None);

        _runId = ExtractRunId(_toolCallResult);
    }

    [Given(@"the ""(.*)"" subcommand is disabled for git in MCP Settings")]
    public async Task GivenTheSubcommandIsDisabledForGitInMcpSettings(string subcommand)
    {
        _gitFixture ??= await GitFixture.CreateAsync();
        _gitFactory ??= new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: 30, rootDirectoryOverride: _gitFixture.RootDirectory);

        // Program.cs already ran LoadMcpPermissionsAsync during this factory's own WebApplicationFactory
        // startup - see McpFileManagementSteps' identical remark for why no manual load is needed here.
        var permissionStore = _gitFactory.Services.GetRequiredService<IMcpPermissionStore>();
        await permissionStore.SetEnabledAsync($"git:{subcommand}", false, CancellationToken.None);
    }

    [When(@"an authenticated caller starts a git_run ""(.*)"" against ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerStartsAGitRunAgainst(string subcommand, string fixtureName) =>
        // No real git repository exists at HangingFixture (it's a plain dotnet fixture) - fine for the
        // cross-kind locking scenario this backs, since the point is the request never gets far enough
        // to invoke git.exe at all: it must be rejected by the repository lock first.
        await StartRunAsync(Client, "git_run", subcommand, fixtureName, timeoutSeconds: null);

    [When(@"an authenticated caller starts a dotnet_run ""(.*)"" with an escaping --results-directory against ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerStartsADotnetRunWithAnEscapingResultsDirectoryAgainst(string subcommand, string fixtureName)
    {
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client,
            "dotnet_run",
            new { arguments = new[] { subcommand, "--results-directory", Path.Combine("..", "..", "escape") }, workingDirectory = fixtureName },
            CommandExecutionServiceFactory.TestBearerToken,
            CancellationToken.None);
        _runId = ExtractRunId(_toolCallResult);
    }

    [Given(@"an in-flight dotnet_run against ""(.*)"" with a (\d+) second timeout")]
    public async Task GivenAnInFlightDotnetRunAgainstWithATimeout(string fixtureName, int timeoutSeconds) =>
        await StartRunAsync(Client, "dotnet_run", "test", fixtureName, timeoutSeconds);

    [When(@"that run reaches a terminal state")]
    public async Task WhenThatRunReachesATerminalState() =>
        await PollUntilTerminalAsync(Client, _runId, TimeSpan.FromSeconds(30));

    [When(@"authenticated dotnet_run calls are started concurrently against ""(.*)"" and ""(.*)""")]
    public void WhenAuthenticatedDotnetRunCallsAreStartedConcurrentlyAgainst(string fixtureA, string fixtureB)
    {
        _concurrentTaskA = StartAndAwaitCompletionAsync(fixtureA);
        _concurrentTaskB = StartAndAwaitCompletionAsync(fixtureB);
    }

    [Then(@"both concurrent runs complete independently")]
    public async Task ThenBothConcurrentRunsCompleteIndependently()
    {
        var results = await Task.WhenAll(_concurrentTaskA!, _concurrentTaskB!);

        foreach (var result in results)
        {
            Assert.False(result.IsToolError, result.ContentText);
        }
    }

    [Then(@"a subsequent dotnet_run against ""(.*)"" is accepted")]
    public async Task ThenASubsequentDotnetRunAgainstIsAccepted(string fixtureName)
    {
        var result = await McpTestClient.CallToolAsync(
            Client, "dotnet_run", new { arguments = new[] { "test" }, workingDirectory = fixtureName }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

        Assert.False(result.IsToolError, result.ContentText);
    }

    private async Task<McpToolCallResult> StartAndAwaitCompletionAsync(string fixtureName)
    {
        var startResult = await McpTestClient.CallToolAsync(
            Client, "dotnet_run", new { arguments = new[] { "test" }, workingDirectory = fixtureName }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        Assert.False(startResult.IsToolError, startResult.ContentText);

        var runId = ExtractRunId(startResult);
        await PollUntilTerminalAsync(Client, runId, TimeSpan.FromSeconds(30));
        return startResult;
    }

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

    private static async Task<JsonElement> PollUntilTerminalAsync(HttpClient client, Guid runId, TimeSpan timeout) =>
        await PollAsync(client, runId, timeout, status => status != "running");

    private static async Task<JsonElement> PollUntilStatusAsync(HttpClient client, Guid runId, string targetStatus, TimeSpan timeout) =>
        await PollAsync(client, runId, timeout, status => status == targetStatus);

    private static async Task<JsonElement> PollAsync(HttpClient client, Guid runId, TimeSpan timeout, Func<string, bool> isDone)
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
        _client?.Dispose();
        _factory.Dispose();
        _gitClient?.Dispose();
        _gitFactory?.Dispose();
        _gitFixture?.Dispose();
    }
}
