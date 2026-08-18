// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Net;
using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpServer.feature</c>.</summary>
[Binding]
public sealed class McpServerSteps : IDisposable
{
    private readonly CommandExecutionServiceFactory _factory = new();
    private HttpClient? _client;
    private HttpResponseMessage? _response;
    private McpToolCallResult? _toolCallResult;

    private HttpClient Client => _client ??= _factory.CreateClient();

    [When(@"an authenticated caller completes the MCP initialize handshake")]
    public async Task WhenAnAuthenticatedCallerCompletesTheMcpInitializeHandshake() =>
        _response = await McpTestClient.InitializeAsync(Client, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the handshake succeeds")]
    public async Task ThenTheHandshakeSucceeds()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        var payload = await McpTestClient.ParseSingleFramePayloadAsync(_response, CancellationToken.None);
        Assert.True(payload.GetProperty("result").TryGetProperty("protocolVersion", out _));
    }

    [When(@"an unauthenticated caller sends an MCP request")]
    public async Task WhenAnUnauthenticatedCallerSendsAnMcpRequest() =>
        _response = await McpTestClient.InitializeAsync(Client, bearerToken: null, CancellationToken.None);

    [When(@"a caller presents an invalid bearer token to the MCP endpoint")]
    public async Task WhenACallerPresentsAnInvalidBearerTokenToTheMcpEndpoint() =>
        _response = await McpTestClient.InitializeAsync(Client, bearerToken: "not-the-configured-token", CancellationToken.None);

    [Then(@"the MCP response is unauthorized")]
    public void ThenTheMcpResponseIsUnauthorized() => Assert.Equal(HttpStatusCode.Unauthorized, _response!.StatusCode);

    [When(@"an authenticated caller lists MCP tools")]
    public async Task WhenAnAuthenticatedCallerListsMcpTools() =>
        _response = await McpTestClient.ListToolsAsync(Client, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the tool list contains exactly ""(.*)""")]
    public async Task ThenTheToolListContainsExactly(string commaSeparatedNames)
    {
        var expected = commaSeparatedNames.Split(", ").ToHashSet();
        var payload = await McpTestClient.ParseSingleFramePayloadAsync(_response!, CancellationToken.None);

        var actual = payload.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .ToHashSet();

        Assert.Equal(expected, actual);
    }

    [When(@"an authenticated caller calls the unknown MCP tool ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerCallsTheUnknownMcpTool(string toolName) =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, toolName, new { }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the MCP call is rejected as an unknown tool")]
    public void ThenTheMcpCallIsRejectedAsAnUnknownTool() =>
        Assert.NotNull(_toolCallResult!.JsonRpcError);

    [When(@"an authenticated caller calls ""(.*)"" with missing required arguments")]
    public async Task WhenAnAuthenticatedCallerCallsWithMissingRequiredArguments(string toolName) =>
        // "arguments" (required) is deliberately omitted - only workingDirectory is supplied.
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, toolName, new { workingDirectory = "SomeRepository" }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the MCP call is rejected without starting a run")]
    public void ThenTheMcpCallIsRejectedWithoutStartingARun()
    {
        // Either a JSON-RPC-level rejection or a tool-level isError result both count as "rejected" -
        // per mcp-server's spec, what matters is that no run started, which the absence of a parsed
        // runId in a successful result already confirms (a successful start always returns one).
        var rejected = _toolCallResult!.JsonRpcError is not null
            || _toolCallResult.IsToolError
            || _toolCallResult.ContentText is null
            || !JsonDocument.Parse(_toolCallResult.ContentText).RootElement.TryGetProperty("runId", out _);

        Assert.True(rejected, "Expected the call to be rejected without a runId in the result.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _response?.Dispose();
        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _factory.Dispose();
    }
}
