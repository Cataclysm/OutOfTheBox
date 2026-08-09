// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpFileLockDiagnostics.feature</c>.</summary>
[Binding]
public sealed class McpFileLockDiagnosticsSteps : IDisposable
{
    private readonly CommandExecutionServiceFactory _factory = new();
    private HttpClient? _client;
    private FileStream? _lockedFile;
    private McpToolCallResult? _toolCallResult;

    private HttpClient Client => _client ??= _factory.CreateClient();

    [Given(@"a file inside a repository is locked open")]
    public void GivenAFileInsideARepositoryIsLockedOpen()
    {
        var path = Path.Combine(CommandExecutionServiceFactory.FindFixturesRoot(), "PassingFixture", "SampleTests.cs");

        // FileShare.Read (no Delete flag) - the same real-lock pattern
        // RepositoryManagementSteps.GivenAFileInsideThatRepositoryIsLockedOpen already establishes.
        _lockedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    [When(@"an authenticated caller calls get_file_lock_info for that file")]
    public async Task WhenAnAuthenticatedCallerCallsGetFileLockInfoForThatFile() =>
        await CallToolAsync("PassingFixture", "SampleTests.cs");

    [When(@"an authenticated caller calls get_file_lock_info for an unlocked file")]
    public async Task WhenAnAuthenticatedCallerCallsGetFileLockInfoForAnUnlockedFile() =>
        await CallToolAsync("PassingFixture", "PassingFixture.csproj");

    [When(@"an authenticated caller calls get_file_lock_info with a path that escapes the repository")]
    public async Task WhenAnAuthenticatedCallerCallsGetFileLockInfoWithAPathThatEscapesTheRepository() =>
        await CallToolAsync("PassingFixture", "../FailingFixture/SampleTests.cs");

    [When(@"an authenticated caller calls get_file_lock_info for a file that does not exist")]
    public async Task WhenAnAuthenticatedCallerCallsGetFileLockInfoForAFileThatDoesNotExist() =>
        await CallToolAsync("PassingFixture", "does-not-exist.txt");

    private async Task CallToolAsync(string repository, string path) =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "get_file_lock_info", new { repository, path }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the result lists this test process as a locking process")]
    public void ThenTheResultListsThisTestProcessAsALockingProcess()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var payload = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;
        var lockingProcesses = payload.GetProperty("lockingProcesses");

        Assert.True(lockingProcesses.GetArrayLength() > 0, "Expected at least one locking process - this test process itself has the file open.");

        var currentProcessId = Environment.ProcessId;
        var found = false;
        foreach (var process in lockingProcesses.EnumerateArray())
        {
            if (process.GetProperty("processId").GetInt32() == currentProcessId)
            {
                found = true;
            }
        }

        Assert.True(found, $"Expected process id {currentProcessId} (this test process) among the locking processes reported.");
    }

    [Then(@"the result has no locking processes")]
    public void ThenTheResultHasNoLockingProcesses()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var payload = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        Assert.Equal(0, payload.GetProperty("lockingProcesses").GetArrayLength());
    }

    [Then(@"the get_file_lock_info call is rejected")]
    public void ThenTheGetFileLockInfoCallIsRejected() =>
        Assert.True(_toolCallResult!.JsonRpcError is not null || _toolCallResult.IsToolError, "Expected the MCP call to be rejected.");

    /// <inheritdoc />
    public void Dispose()
    {
        _lockedFile?.Dispose();
        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _factory.Dispose();
    }
}
