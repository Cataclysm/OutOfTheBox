// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.BehaviorTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpPermissions.feature</c>.</summary>
[Binding]
public sealed class McpPermissionsSteps : IDisposable
{
    private readonly CommandExecutionServiceFactory _factory = new();
    private HttpClient? _client;
    private McpToolCallResult? _toolCallResult;

    private HttpClient Client => _client ??= _factory.CreateClient();

    [Given(@"the ""([^""]*)"" tool is disabled in MCP Settings")]
    public async Task GivenTheToolIsDisabledInMcpSettings(string key) =>
        await _factory.Services.GetRequiredService<IMcpPermissionStore>().SetEnabledAsync(key, false, CancellationToken.None);

    [Given(@"the ""([^""]*)"" tool is enabled in MCP Settings")]
    public async Task GivenTheToolIsEnabledInMcpSettings(string key) =>
        await _factory.Services.GetRequiredService<IMcpPermissionStore>().SetEnabledAsync(key, true, CancellationToken.None);

    [When(@"an authenticated caller calls get_mcp_permissions")]
    public async Task WhenAnAuthenticatedCallerCallsGetMcpPermissions() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "get_mcp_permissions", new { }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the result includes an entry for ""([^""]*)"" that is enabled")]
    public void ThenTheResultIncludesAnEntryForThatIsEnabled(string key) =>
        AssertEntryEnabled(key, expectedEnabled: true);

    [Then(@"the result includes an entry for ""([^""]*)"" that is disabled")]
    public void ThenTheResultIncludesAnEntryForThatIsDisabled(string key) =>
        AssertEntryEnabled(key, expectedEnabled: false);

    private void AssertEntryEnabled(string key, bool expectedEnabled)
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var permissions = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement.GetProperty("permissions");

        var entry = permissions.EnumerateArray().Single(e => e.GetProperty("key").GetString() == key);
        Assert.Equal(expectedEnabled, entry.GetProperty("enabled").GetBoolean());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _factory.Dispose();
    }
}
