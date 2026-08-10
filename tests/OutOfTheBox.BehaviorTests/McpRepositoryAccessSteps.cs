// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpRepositoryAccess.feature</c>.</summary>
[Binding]
public sealed class McpRepositoryAccessSteps : IDisposable
{
    private GitFixture? _gitFixture;
    private CommandExecutionServiceFactory? _factory;
    private HttpClient? _client;
    private McpToolCallResult? _toolCallResult;
    private Guid _runId;

    private async Task EnsureFactoryAsync()
    {
        if (_factory is not null)
        {
            return;
        }

        _gitFixture = await GitFixture.CreateAsync();
        _factory = new CommandExecutionServiceFactory(rootDirectoryOverride: _gitFixture.RootDirectory);
    }

    private HttpClient Client => _client ??= _factory!.CreateClient();

    private string SourceRepositoryPath => Path.Combine(_gitFixture!.RootDirectory, "GitFixture");

    [Given(@"an existing repository named ""(.*)"" is on disk for MCP access")]
    public async Task GivenAnExistingRepositoryNamedIsOnDiskForMcpAccess(string name)
    {
        await EnsureFactoryAsync();
        Directory.CreateDirectory(Path.Combine(_gitFixture!.RootDirectory, name));
        await File.WriteAllTextAsync(Path.Combine(_gitFixture.RootDirectory, name, "marker.txt"), "present");
    }

    [When(@"an authenticated caller calls list_repositories")]
    public async Task WhenAnAuthenticatedCallerCallsListRepositories()
    {
        await EnsureFactoryAsync();
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "list_repositories", new { }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
    }

    [Then(@"the result includes ""(.*)"" with its size and git status")]
    public void ThenTheResultIncludesWithItsSizeAndGitStatus(string name)
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var summaries = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        var match = summaries.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        Assert.True(match.GetProperty("statsComputed").GetBoolean());
        Assert.True(match.GetProperty("totalSizeBytes").GetInt64() > 0);
    }

    [When(@"an authenticated caller calls clone_repository for the fixture repository under ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerCallsCloneRepositoryForTheFixtureRepositoryUnder(string name)
    {
        await EnsureFactoryAsync();
        await CallCloneAsync(SourceRepositoryPath, name);
    }

    [When(@"an authenticated caller calls clone_repository with the name ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerCallsCloneRepositoryWithTheName(string name)
    {
        await EnsureFactoryAsync();
        await CallCloneAsync(SourceRepositoryPath, name);
    }

    private async Task CallCloneAsync(string url, string name)
    {
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "clone_repository", new { url, name }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

        if (!_toolCallResult.IsToolError && _toolCallResult.JsonRpcError is null)
        {
            _runId = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement.GetProperty("runId").GetGuid();
        }
    }

    [Then(@"an MCP clone run id is returned")]
    public void ThenAnMcpCloneRunIdIsReturned() => Assert.NotEqual(Guid.Empty, _runId);

    [Then(@"the clone_repository call is rejected")]
    public void ThenTheCloneRepositoryCallIsRejected() =>
        Assert.True(_toolCallResult!.IsToolError || _toolCallResult.JsonRpcError is not null, "Expected clone_repository to be rejected.");

    [Then(@"""(.*)"" eventually appears via list_repositories")]
    public async Task ThenEventuallyAppearsViaListRepositories(string name)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var result = await McpTestClient.CallToolAsync(
                Client, "list_repositories", new { }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
            var summaries = JsonDocument.Parse(result.ContentText!).RootElement;

            if (summaries.EnumerateArray().Any(s => s.GetProperty("name").GetString() == name))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"'{name}' did not appear via list_repositories in time.");
    }

    [When(@"the caller cancels that clone via cancel_run")]
    public async Task WhenTheCallerCancelsThatCloneViaCancelRun() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "cancel_run", new { runId = _runId }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"cancel_run does not reject the clone's run id as unknown")]
    public async Task ThenCancelRunDoesNotRejectTheCloneSRunIdAsUnknown()
    {
        // Per mcp-repository-access's spec, what distinguishes MCP from the REST cancel endpoint is
        // that a clone's run id is recognized at all (never treated as unknown) - cancel_run's own
        // immediate response can still legitimately read "running" (it only requests cancellation,
        // it doesn't wait for the background continuation to observe and persist it - see
        // CommandExecutionMcpTools.CancelRunAsync's own remarks), so this polls read_run_output to
        // confirm the run genuinely settles to "cancelled" or "completed" (whichever the clone's own
        // timing produced), never staying stuck or erroring as unknown.
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        Assert.Null(_toolCallResult.JsonRpcError);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        string? status;
        do
        {
            var result = await McpTestClient.CallToolAsync(
                Client, "read_run_output", new { runId = _runId, offset = 0 }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
            Assert.False(result.IsToolError, result.ContentText);
            status = JsonDocument.Parse(result.ContentText!).RootElement.GetProperty("status").GetString();

            if (status != "running")
            {
                break;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Run {_runId} stayed 'running' and never settled.");
            }

            await Task.Delay(100);
        }
        while (true);

        Assert.True(status is "cancelled" or "completed", $"Expected 'cancelled' or 'completed', got '{status}'.");
    }

    [When(@"an authenticated caller calls delete_repository for ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerCallsDeleteRepositoryFor(string name)
    {
        await EnsureFactoryAsync();
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "delete_repository", new { name }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
    }

    [Then(@"delete_repository reports success and ""(.*)"" no longer appears via list_repositories")]
    public async Task ThenDeleteRepositoryReportsSuccessAndNoLongerAppearsViaListRepositories(string name)
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);

        var result = await McpTestClient.CallToolAsync(
            Client, "list_repositories", new { }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        var summaries = JsonDocument.Parse(result.ContentText!).RootElement;

        Assert.DoesNotContain(summaries.EnumerateArray(), s => s.GetProperty("name").GetString() == name);
    }

    [Then(@"the delete_repository call is rejected as not found")]
    public void ThenTheDeleteRepositoryCallIsRejectedAsNotFound()
    {
        Assert.True(_toolCallResult!.IsToolError, "Expected delete_repository to be rejected.");
        Assert.Contains("does not exist", _toolCallResult.ContentText, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _factory?.Dispose();
        _gitFixture?.Dispose();
    }
}
