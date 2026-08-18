// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Diagnostics;
using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using OutOfTheBox.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    [When(@"""([^""]*)""'s credential-helper entry is deleted out of band")]
    public static Task WhenAHostsCredentialHelperEntryIsDeletedOutOfBand(string host) =>
        // Simulates the exact real-world loss CredentialSyncService exists to repair - the DB row
        // (authorize_git_host already wrote it) survives, but git's own credential-helper entry
        // (the file-based test store - see TestGitCredentialConfigSetup) does not, the same shape a
        // real uninstall-then-reinstall's recreated service account produces against the real
        // Windows-Credential-Manager-backed helper.
        RunGitCredentialAsync("reject", $"protocol=https\nhost={host}\n\n");

    [When(@"the background credential sync service runs a sync sweep")]
    public async Task WhenTheBackgroundCredentialSyncServiceRunsASyncSweep()
    {
        var syncService = Factory.Services.GetServices<IHostedService>().OfType<CredentialSyncService>().Single();
        await syncService.SyncAllOnceAsync(CancellationToken.None);
    }

    [Then(@"git credential fill for ""([^""]*)"" returns the originally authorized token")]
    public static async Task ThenGitCredentialFillForReturnsTheOriginallyAuthorizedToken(string host)
    {
        var fillOutput = await RunGitCredentialAsync("fill", $"protocol=https\nhost={host}\n\n");

        Assert.Contains("password=test-token-1", fillOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs `git credential &lt;subcommand&gt;` with <paramref name="standardInput"/> piped in, against
    /// the same throwaway file-based credential helper <see cref="TestGitCredentialConfigSetup"/>
    /// points every git invocation in this test process at - <see cref="GitFixture.RunGitAsync"/>
    /// doesn't support stdin redirection, which `credential reject`/`fill` need.
    /// </summary>
    private static async Task<string> RunGitCredentialAsync(string subcommand, string standardInput)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("credential");
        process.StartInfo.ArgumentList.Add(subcommand);

        process.Start();
        await process.StandardInput.WriteAsync(standardInput);
        process.StandardInput.Close();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return stdout;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _factory?.Dispose();
    }
}
