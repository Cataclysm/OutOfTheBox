// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Text.Json;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.BehaviorTests.Support;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpFileManagement.feature</c>.</summary>
[Binding]
public sealed class McpFileManagementSteps : IDisposable
{
    private const string RepositoryName = "repo";

    // A fresh scratch root, not the shared checked-in fixtures - find_files/get_file_info are
    // read-only, but delete_path genuinely mutates the filesystem, and a single shared fixture
    // would either need per-scenario cleanup or accumulate garbage across runs. One temp directory
    // covers every scenario in this feature, mirroring McpRepositoryAccessSteps' own GitFixture-based
    // scratch root for the same reason, minus the real-git-repository overhead this feature doesn't need.
    private readonly string _scratchRoot = Directory.CreateTempSubdirectory("oob-mcp-filemgmt-").FullName;
    private CommandExecutionServiceFactory? _factory;
    private HttpClient? _client;
    private McpToolCallResult? _toolCallResult;

    private CommandExecutionServiceFactory Factory => _factory ??= new CommandExecutionServiceFactory(rootDirectoryOverride: _scratchRoot);

    private HttpClient Client => _client ??= Factory.CreateClient();

    private string RepositoryPath => Path.Combine(_scratchRoot, RepositoryName);

    [Given(@"a repository with nested files at ""([^""]*)""")]
    public void GivenARepositoryWithNestedFilesAt(string relativePath) =>
        CreateFile(relativePath);

    [Given(@"a repository with (\d+) files matching ""([^""]*)""")]
    public void GivenARepositoryWithFilesMatching(int count, string pattern)
    {
        var extension = pattern.TrimStart('*');
        for (var i = 0; i < count; i++)
        {
            CreateFile($"file{i}{extension}");
        }
    }

    [Given(@"the configured MCP find_files result cap is (\d+)")]
    public void GivenTheConfiguredMcpFindFilesResultCapIs(int cap) =>
        _factory = new CommandExecutionServiceFactory(rootDirectoryOverride: _scratchRoot, mcpMaxFindFilesResultsOverride: cap);

    [Given(@"the find_files tool is disabled in MCP Settings")]
    public async Task GivenTheFindFilesToolIsDisabledInMcpSettings()
    {
        // Program.cs already ran LoadMcpPermissionsAsync during this WebApplicationFactory's own
        // startup (the same "everything before app.Run() genuinely executes" mechanism migrations
        // already rely on in this test suite) - no need to call it again here.
        var permissionStore = Factory.Services.GetRequiredService<IMcpPermissionStore>();
        await permissionStore.SetEnabledAsync("find_files", false, CancellationToken.None);
    }

    private void CreateFile(string relativePath)
    {
        var fullPath = Path.Combine(RepositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "content");
    }

    [When(@"an authenticated caller calls find_files with pattern ""([^""]*)""")]
    public async Task WhenAnAuthenticatedCallerCallsFindFilesWithPattern(string pattern) =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "find_files", new { repository = RepositoryName, pattern }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [When(@"an authenticated caller calls find_files with no pattern")]
    public async Task WhenAnAuthenticatedCallerCallsFindFilesWithNoPattern() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "find_files", new { repository = RepositoryName }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the find_files result lists exactly ""([^""]*)""")]
    public void ThenTheFindFilesResultListsExactly(string expectedPaths) =>
        Assert.Equal(SplitList(expectedPaths).Order(StringComparer.Ordinal), ExtractRelativePaths().Order(StringComparer.Ordinal));

    [Then(@"the find_files result includes ""([^""]*)""")]
    public void ThenTheFindFilesResultIncludes(string expectedPaths)
    {
        var paths = ExtractRelativePaths();
        foreach (var expected in SplitList(expectedPaths))
        {
            Assert.Contains(expected, paths);
        }
    }

    private static IReadOnlyList<string> SplitList(string commaSeparated) =>
        [.. commaSeparated.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    [Then(@"the matched entry ""([^""]*)"" is a directory")]
    public void ThenTheMatchedEntryIsADirectory(string relativePath)
    {
        var entries = GetResultProperty("entries");
        var match = entries.EnumerateArray().Single(e => e.GetProperty("relativePath").GetString() == relativePath);
        Assert.True(match.GetProperty("isDirectory").GetBoolean());
    }

    [Then(@"the find_files result has exactly (\d+) entries and is marked truncated")]
    public void ThenTheFindFilesResultHasExactlyEntriesAndIsMarkedTruncated(int expectedCount)
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var payload = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;
        Assert.Equal(expectedCount, payload.GetProperty("entries").GetArrayLength());
        Assert.True(payload.GetProperty("truncated").GetBoolean());
    }

    private IReadOnlyList<string> ExtractRelativePaths() =>
        [.. GetResultProperty("entries").EnumerateArray().Select(e => e.GetProperty("relativePath").GetString()!)];

    private JsonElement GetResultProperty(string propertyName)
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        return JsonDocument.Parse(_toolCallResult.ContentText!).RootElement.GetProperty(propertyName);
    }

    [When(@"an authenticated caller calls get_file_info for ""([^""]*)""")]
    public async Task WhenAnAuthenticatedCallerCallsGetFileInfoFor(string path) =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "get_file_info", new { repository = RepositoryName, path }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the get_file_info result reports a file with a size and owner")]
    public void ThenTheGetFileInfoResultReportsAFileWithASizeAndOwner()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var result = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        Assert.False(result.GetProperty("isDirectory").GetBoolean());
        Assert.True(result.GetProperty("sizeBytes").GetInt64() > 0);
        Assert.False(string.IsNullOrEmpty(result.GetProperty("owner").GetString()));
    }

    [Then(@"the get_file_info result reports a directory with no size and no lock status")]
    public void ThenTheGetFileInfoResultReportsADirectoryWithNoSizeAndNoLockStatus()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var result = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        Assert.True(result.GetProperty("isDirectory").GetBoolean());
        AssertPropertyAbsentOrNull(result, "sizeBytes");
        AssertPropertyAbsentOrNull(result, "isLocked");
    }

    // The MCP result serializer omits null-valued properties entirely rather than emitting a JSON
    // `null` literal, so "no value" must be checked as "missing OR explicitly null", not just one.
    private static void AssertPropertyAbsentOrNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            Assert.Equal(JsonValueKind.Null, property.ValueKind);
        }
    }

    [Then(@"the get_file_info call is rejected as a confinement violation")]
    public void ThenTheGetFileInfoCallIsRejectedAsAConfinementViolation() =>
        AssertRejectedContaining("escapes repository");

    [Then(@"the get_file_info call is rejected as not found")]
    public void ThenTheGetFileInfoCallIsRejectedAsNotFound() =>
        AssertRejectedContaining("does not exist");

    [When(@"an authenticated caller calls delete_path for ""([^""]*)""")]
    public async Task WhenAnAuthenticatedCallerCallsDeletePathFor(string path) =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "delete_path", new { repository = RepositoryName, path }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"delete_path reports success and ""([^""]*)"" no longer exists on disk")]
    public void ThenDeletePathReportsSuccessAndNoLongerExistsOnDisk(string relativePath)
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var fullPath = Path.Combine(RepositoryPath, relativePath);
        Assert.False(Directory.Exists(fullPath) || File.Exists(fullPath));
    }

    [Then(@"the delete_path call is rejected")]
    public void ThenTheDeletePathCallIsRejected() =>
        Assert.True(_toolCallResult!.IsToolError || _toolCallResult.JsonRpcError is not null, "Expected delete_path to be rejected.");

    [Then(@"the find_files call is rejected")]
    public void ThenTheFindFilesCallIsRejected() =>
        Assert.True(_toolCallResult!.IsToolError || _toolCallResult.JsonRpcError is not null, "Expected find_files to be rejected.");

    [Then(@"the delete_path call is rejected as a confinement violation")]
    public void ThenTheDeletePathCallIsRejectedAsAConfinementViolation() =>
        AssertRejectedContaining("escapes repository");

    [Then(@"the delete_path call is rejected as not found")]
    public void ThenTheDeletePathCallIsRejectedAsNotFound() =>
        AssertRejectedContaining("does not exist");

    [Then(@"a RepositoryFileDelete run appears in history with outcome ""([^""]*)""")]
    public async Task ThenARepositoryFileDeleteRunAppearsInHistoryWithOutcome(string expectedOutcome)
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);

        // History isn't REST-reachable in this service (dashboard-only), so this asserts through
        // the same IRunRepository the dashboard itself reads from, resolved from the running host's
        // own service provider - matching RepositoryManagementSteps' own precedent for asserting on
        // persisted history state a BDD scenario has no MCP/REST surface to query directly.
        using var scope = Factory.Services.CreateScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        var runs = await runRepository.ListAsync(
            new RunQuery { Kinds = [RunKind.RepositoryFileDelete] },
            CancellationToken.None);

        var run = Assert.Single(runs);
        Assert.Equal(expectedOutcome, run.Outcome.ToString());
    }

    private void AssertRejectedContaining(string expectedSubstring)
    {
        Assert.True(_toolCallResult!.IsToolError, "Expected the MCP call to be rejected.");
        Assert.Contains(expectedSubstring, _toolCallResult.ContentText, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _factory?.Dispose();

        if (Directory.Exists(_scratchRoot))
        {
            try
            {
                Directory.Delete(_scratchRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
