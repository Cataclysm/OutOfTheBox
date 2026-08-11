// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpGitCredentials.feature</c>.</summary>
[Binding]
public sealed class McpGitCredentialsSteps : IDisposable
{
    private CommandExecutionServiceFactory? _factory;
    private HttpClient? _client;
    private McpToolCallResult? _toolCallResult;

    private CommandExecutionServiceFactory Factory => _factory ??= new CommandExecutionServiceFactory();

    private HttpClient Client => _client ??= Factory.CreateClient();

    [Given(@"a host ""([^""]*)"" has been authorized with a token")]
    public async Task GivenAHostHasBeenAuthorizedWithAToken(string host)
    {
        var result = await McpTestClient.CallToolAsync(
            Client, "authorize_git_host", new { host, token = "test-token-1" }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        Assert.False(result.IsToolError, result.ContentText);
    }

    [When(@"an authenticated caller calls authorize_git_host for ""([^""]*)"" with a token")]
    public async Task WhenAnAuthenticatedCallerCallsAuthorizeGitHostForWithAToken(string host) =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "authorize_git_host", new { host, token = "test-token-1" }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [When(@"an authenticated caller calls authorize_git_host for ""([^""]*)"" with a different token")]
    public async Task WhenAnAuthenticatedCallerCallsAuthorizeGitHostForWithADifferentToken(string host) =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "authorize_git_host", new { host, token = "test-token-2" }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"authorize_git_host reports success")]
    public void ThenAuthorizeGitHostReportsSuccess()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var result = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;
        Assert.True(result.GetProperty("authorized").GetBoolean());
    }

    [When(@"an authenticated caller calls list_authorized_git_hosts")]
    public async Task WhenAnAuthenticatedCallerCallsListAuthorizedGitHosts() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "list_authorized_git_hosts", new { }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the result includes ""([^""]*)"" with an authorization timestamp and no token value")]
    public void ThenTheResultIncludesWithAnAuthorizationTimestampAndNoTokenValue(string host)
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        Assert.DoesNotContain("test-token", _toolCallResult.ContentText, StringComparison.Ordinal);

        var hosts = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;
        var match = hosts.EnumerateArray().Single(h => h.GetProperty("host").GetString() == host);
        Assert.True(match.TryGetProperty("authorizedAtUtc", out var timestamp));
        Assert.NotEqual(default, timestamp.GetDateTimeOffset());
    }

    [Then(@"the result is an empty list")]
    public void ThenTheResultIsAnEmptyList()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var hosts = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;
        Assert.Empty(hosts.EnumerateArray());
    }

    [Then(@"list_authorized_git_hosts lists ""([^""]*)"" exactly once")]
    public async Task ThenListAuthorizedGitHostsListsExactlyOnce(string host)
    {
        var result = await McpTestClient.CallToolAsync(
            Client, "list_authorized_git_hosts", new { }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        Assert.False(result.IsToolError, result.ContentText);

        var hosts = JsonDocument.Parse(result.ContentText!).RootElement;
        Assert.Single(hosts.EnumerateArray(), h => h.GetProperty("host").GetString() == host);
    }

    [When(@"an authenticated caller calls revoke_git_host_authorization for ""([^""]*)""")]
    public async Task WhenAnAuthenticatedCallerCallsRevokeGitHostAuthorizationFor(string host) =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "revoke_git_host_authorization", new { host }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"revoke_git_host_authorization reports success")]
    public void ThenRevokeGitHostAuthorizationReportsSuccess()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var result = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;
        Assert.True(result.GetProperty("revoked").GetBoolean());
    }

    [Then(@"""([^""]*)"" no longer appears via list_authorized_git_hosts")]
    public async Task ThenNoLongerAppearsViaListAuthorizedGitHosts(string host)
    {
        var result = await McpTestClient.CallToolAsync(
            Client, "list_authorized_git_hosts", new { }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        Assert.False(result.IsToolError, result.ContentText);

        var hosts = JsonDocument.Parse(result.ContentText!).RootElement;
        Assert.DoesNotContain(hosts.EnumerateArray(), h => h.GetProperty("host").GetString() == host);
    }

    [Then(@"the revoke_git_host_authorization call is rejected as nothing to revoke")]
    public void ThenTheRevokeGitHostAuthorizationCallIsRejectedAsNothingToRevoke()
    {
        Assert.True(_toolCallResult!.IsToolError, "Expected revoke_git_host_authorization to be rejected.");
        Assert.Contains("nothing to revoke", _toolCallResult.ContentText, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _factory?.Dispose();
    }
}
