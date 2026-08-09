// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OutOfTheBox.BehaviorTests.Support;

/// <summary>The result of a <c>tools/call</c> request: either a JSON-RPC-level error (the call itself was rejected - unknown tool, bad schema binding) or the tool's own result content/error.</summary>
public sealed record McpToolCallResult(HttpResponseMessage Response, JsonElement? JsonRpcError, string? ContentText, bool IsToolError);

/// <summary>
/// Minimal test-only MCP Streamable HTTP client, mirroring <see cref="SseTestClient"/>'s own
/// hand-rolled-over-off-the-shelf-library precedent for this project's behavior tests: sends one
/// JSON-RPC request per call to <c>/mcp</c> and parses the single response frame the server sends
/// back in stateless mode (either a plain JSON body or one <c>event: message</c>/<c>data: {...}</c>
/// SSE frame - the Streamable HTTP transport may choose either shape per request, so both are
/// handled). Stateless mode means each request is independent - no prior <c>initialize</c> call is
/// required before <c>tools/list</c>/<c>tools/call</c>, confirmed live against the real running
/// service during Section 4-6 implementation.
/// </summary>
public static class McpTestClient
{
    /// <summary>Sends the MCP <c>initialize</c> handshake.</summary>
    public static Task<HttpResponseMessage> InitializeAsync(HttpClient client, string? bearerToken, CancellationToken cancellationToken) =>
        SendAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "behavior-tests", version = "0.0.1" },
                },
            },
            bearerToken,
            cancellationToken);

    /// <summary>Sends a <c>tools/list</c> request.</summary>
    public static Task<HttpResponseMessage> ListToolsAsync(HttpClient client, string? bearerToken, CancellationToken cancellationToken) =>
        SendAsync(client, new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } }, bearerToken, cancellationToken);

    /// <summary>Sends a <c>tools/call</c> request for <paramref name="toolName"/> with <paramref name="arguments"/>, and parses the result.</summary>
    public static async Task<McpToolCallResult> CallToolAsync(
        HttpClient client, string toolName, object arguments, string? bearerToken, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            client,
            new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = toolName, arguments } },
            bearerToken,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new McpToolCallResult(response, null, null, false);
        }

        var payload = await ParseSingleFramePayloadAsync(response, cancellationToken);

        if (payload.TryGetProperty("error", out var jsonRpcError))
        {
            return new McpToolCallResult(response, jsonRpcError, null, false);
        }

        var result = payload.GetProperty("result");
        var isError = result.TryGetProperty("isError", out var isErrorElement) && isErrorElement.GetBoolean();
        var contentText = result.GetProperty("content")[0].GetProperty("text").GetString();

        return new McpToolCallResult(response, null, contentText, isError);
    }

    /// <summary>Posts a raw JSON-RPC request to <c>/mcp</c>, without parsing the response - for scenarios only asserting on the HTTP status code (e.g. an unauthenticated request).</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, object jsonRpcRequest, string? bearerToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(jsonRpcRequest) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>Parses a response body that is either a plain JSON-RPC object or one SSE <c>data:</c> frame carrying one.</summary>
    public static async Task<JsonElement> ParseSingleFramePayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var dataLine = body.Split('\n').FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal));
        var json = dataLine is not null ? dataLine["data:".Length..].Trim() : body;

        return JsonDocument.Parse(json).RootElement;
    }
}
