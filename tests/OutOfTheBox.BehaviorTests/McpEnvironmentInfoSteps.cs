// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Text.Json;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.BehaviorTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpEnvironmentInfo.feature</c>.</summary>
[Binding]
public sealed class McpEnvironmentInfoSteps : IDisposable
{
    private readonly CommandExecutionServiceFactory _factory = new();
    private HttpClient? _client;
    private IServiceScope? _scope;
    private McpToolCallResult? _toolCallResult;

    private HttpClient Client => _client ??= _factory.CreateClient();

    private IServiceScope Scope => _scope ??= _factory.Services.CreateScope();

    [When(@"an authenticated caller calls get_environment_info")]
    public async Task WhenAnAuthenticatedCallerCallsGetEnvironmentInfo() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "get_environment_info", new { }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the result includes this host's real installed toolchain, SDKs, and disk space")]
    public void ThenTheResultIncludesThisHostSRealInstalledToolchainSdksAndDiskSpace()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var payload = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("dotnetVersion").GetString()));
        Assert.True(payload.GetProperty("installedSdks").GetArrayLength() > 0, "Expected at least one installed SDK - this machine has the .NET SDK this test itself is running under.");

        var diskSpace = payload.GetProperty("rootDirectoryDiskSpace");
        Assert.True(diskSpace.GetProperty("totalBytes").GetInt64() > 0);
        Assert.True(diskSpace.GetProperty("availableFreeBytes").GetInt64() > 0);
    }

    [Then(@"the call succeeds regardless of what the workload listing reports")]
    public void ThenTheCallSucceedsRegardlessOfWhatTheWorkloadListingReports()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var payload = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        // Just needs to be present as an array (possibly empty) - never absent, never an error,
        // regardless of what this host's actual workload listing looks like.
        Assert.Equal(JsonValueKind.Array, payload.GetProperty("installedWorkloadIds").ValueKind);
    }

    [Then(@"the reported dotnet and git versions match the dashboard's own installed-tool-versions provider")]
    public async Task ThenTheReportedDotnetAndGitVersionsMatchTheDashboardSOwnInstalledToolVersionsProvider()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var payload = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        var provider = Scope.ServiceProvider.GetRequiredService<IInstalledToolVersionsProvider>();
        var expected = await provider.GetVersionsAsync(CancellationToken.None);

        Assert.Equal(expected.DotnetVersion, payload.GetProperty("dotnetVersion").GetString());
        Assert.Equal(expected.GitVersion, payload.GetProperty("gitVersion").GetString());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _scope?.Dispose();
        _factory.Dispose();
    }
}
