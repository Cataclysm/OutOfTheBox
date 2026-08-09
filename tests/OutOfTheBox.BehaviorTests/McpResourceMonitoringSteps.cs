// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.BehaviorTests.Support;
using OutOfTheBox.Infrastructure.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>McpResourceMonitoring.feature</c>.</summary>
[Binding]
public sealed class McpResourceMonitoringSteps : IDisposable
{
    private readonly CommandExecutionServiceFactory _factory = new(defaultExecutionTimeoutSeconds: 300);
    private HttpClient? _client;
    private IServiceScope? _scope;

    private McpToolCallResult? _toolCallResult;
    private Guid _runId;

    private HttpClient Client => _client ??= _factory.CreateClient();

    private IServiceScope Scope => _scope ??= _factory.Services.CreateScope();

    private RunRegistry RunRegistry => Scope.ServiceProvider.GetRequiredService<RunRegistry>();

    // HostResourceSamplerService is registered only via AddHostedService<T> (Program.cs) - not its
    // own concrete type - but that registration still resolves through IHostedService, the same way
    // ASP.NET Core itself finds every hosted service to start. Its own TickAsync is public exactly so
    // a test can force one deterministic tick instead of waiting on its real ~3s PeriodicTimer.
    private HostResourceSamplerService Sampler =>
        _factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<HostResourceSamplerService>().Single();

    [Given(@"a dotnet_run is in flight against a long-running fixture")]
    public async Task GivenADotnetRunIsInFlightAgainstALongRunningFixture()
    {
        var result = await McpTestClient.CallToolAsync(
            Client, "dotnet_run", new { arguments = new[] { "test" }, workingDirectory = "HangingFixture" }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        Assert.False(result.IsToolError, result.ContentText);
        _runId = JsonDocument.Parse(result.ContentText!).RootElement.GetProperty("runId").GetGuid();

        // dotnet_run returns once the run is accepted (lock acquired) but before the spawned
        // dotnet.exe process id is necessarily recorded - poll until RunRegistry actually has it,
        // the same precaution HostResourceMonitoringSteps already takes for the same reason.
        for (var i = 0; i < 100 && !RunRegistry.GetTrackedProcessRoots().Any(r => r.RunId == _runId); i++)
        {
            await Task.Delay(50, CancellationToken.None);
        }
    }

    [Given(@"the resource sampler persists a sample for that run")]
    public async Task GivenTheResourceSamplerPersistsASampleForThatRun() => await Sampler.TickAsync(CancellationToken.None);

    [When(@"an authenticated caller calls get_run_resources for that run")]
    [When(@"an authenticated caller calls get_run_resources for that run before any sampler tick")]
    public async Task WhenAnAuthenticatedCallerCallsGetRunResourcesForThatRun() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "get_run_resources", new { runId = _runId }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [When(@"an authenticated caller calls get_run_resources with an unknown run id")]
    public async Task WhenAnAuthenticatedCallerCallsGetRunResourcesWithAnUnknownRunId() =>
        _toolCallResult = await McpTestClient.CallToolAsync(
            Client, "get_run_resources", new { runId = Guid.NewGuid() }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);

    [Then(@"the result includes at least one sample point and a trend summary")]
    public void ThenTheResultIncludesAtLeastOneSamplePointAndATrendSummary()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var payload = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        var points = payload.GetProperty("points");
        Assert.True(points.GetArrayLength() > 0, "Expected at least one resource sample point.");

        var firstPoint = points[0];
        Assert.True(firstPoint.GetProperty("cpuPercent").GetDouble() >= 0);
        Assert.True(firstPoint.GetProperty("ramBytes").GetInt64() > 0, "Expected a positive RAM figure for a real dotnet.exe process.");

        var trend = payload.GetProperty("trend");
        Assert.NotEqual(JsonValueKind.Null, trend.ValueKind);
        Assert.True(trend.GetProperty("peakCpuPercent").GetDouble() >= 0);
    }

    [Then(@"the result has no sample points and no trend summary")]
    public void ThenTheResultHasNoSamplePointsAndNoTrendSummary()
    {
        Assert.False(_toolCallResult!.IsToolError, _toolCallResult.ContentText);
        var payload = JsonDocument.Parse(_toolCallResult.ContentText!).RootElement;

        Assert.Equal(0, payload.GetProperty("points").GetArrayLength());

        // A null "trend" may be serialized as an explicit JSON null, or omitted from the payload
        // entirely, depending on the MCP SDK's serializer settings - both mean "no trend", so
        // TryGetProperty (rather than GetProperty, which throws for a missing property) covers both.
        Assert.True(
            !payload.TryGetProperty("trend", out var trend) || trend.ValueKind == JsonValueKind.Null,
            "Expected no trend summary.");
    }

    [Then(@"the get_run_resources call is rejected")]
    public void ThenTheGetRunResourcesCallIsRejected() =>
        Assert.True(_toolCallResult!.JsonRpcError is not null || _toolCallResult.IsToolError, "Expected the MCP call to be rejected.");

    /// <inheritdoc />
    public void Dispose()
    {
        // Best-effort cleanup of the hung fixture's process, the same precaution
        // HostResourceMonitoringSteps/McpCommandExecutionSteps already take.
        if (_scope is not null)
        {
            RunRegistry.TryCancel(_runId);
        }

        _toolCallResult?.Response.Dispose();
        _client?.Dispose();
        _scope?.Dispose();
        _factory.Dispose();
    }
}
