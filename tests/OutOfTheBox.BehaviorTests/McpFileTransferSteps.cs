// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpFileTransfer.feature</c>.</summary>
[Binding]
public sealed class McpFileTransferSteps : IDisposable
{
    private CommandExecutionServiceFactory? _factory;
    private HttpClient? _client;
    private McpToolCallResult? _toolCallResult;

    private CommandExecutionServiceFactory Factory => _factory ??= new CommandExecutionServiceFactory();

    private HttpClient Client => _client ??= Factory.CreateClient();

    [Given(@"the configured MCP file transfer limit is (\d+) bytes")]
    public void GivenTheConfiguredMcpFileTransferLimitIsBytes(long limitBytes) =>
        _factory = new CommandExecutionServiceFactory(mcpMaxFileTransferBytesOverride: limitBytes);

    [When(@"an authenticated caller calls transfer_file for ""(.*)"" in ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerCallsTransferFileForIn(string path, string repository) =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "transfer_file", new { repository, path }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the transferred content matches the source file exactly")]
    public async Task ThenTheTransferredContentMatchesTheSourceFileExactly()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var result = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        var sourcePath = Path.Combine(CommandExecutionServiceFactory.FindFixturesRoot(), "PassingFixture", "SampleTests.cs");
        var expectedBytes = await File.ReadAllBytesAsync(sourcePath);

        Assert.Equal(expectedBytes, Convert.FromBase64String(result.GetProperty("contentBase64").GetString()!));
        Assert.Equal(expectedBytes.LongLength, result.GetProperty("sizeBytes").GetInt64());
    }

    [Then(@"the transfer_file call is rejected as a confinement violation")]
    public void ThenTheTransferFileCallIsRejectedAsAConfinementViolation() =>
        AssertRejectedContaining("escapes repository");

    [Then(@"the transfer_file call is rejected as not found")]
    public void ThenTheTransferFileCallIsRejectedAsNotFound() =>
        AssertRejectedContaining("does not exist");

    [Then(@"the transfer_file call is rejected as too large")]
    public void ThenTheTransferFileCallIsRejectedAsTooLarge() =>
        AssertRejectedContaining("exceeding the configured limit");

    private void AssertRejectedContaining(string expectedSubstring)
    {
        Assert.True(_toolCallResult!.IsToolError, "Expected transfer_file to be rejected.");
        Assert.Contains(expectedSubstring, _toolCallResult.ContentText, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _factory?.Dispose();
    }
}
