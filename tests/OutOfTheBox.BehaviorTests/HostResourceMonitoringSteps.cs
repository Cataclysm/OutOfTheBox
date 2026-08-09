// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Monitoring;
using OutOfTheBox.BehaviorTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>HostResourceMonitoring.feature</c>.</summary>
[Binding]
public sealed class HostResourceMonitoringSteps : IDisposable
{
    private CommandExecutionServiceFactory? _factory;
    private IServiceScope? _scope;
    private HttpClient? _inFlightClient;
    private Guid _runId;
    private ResourceSnapshot? _snapshot;
    private bool _killAccepted;

    private CommandExecutionServiceFactory Factory => _factory ??= new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: 300);

    private IServiceScope Scope => _scope ??= Factory.Services.CreateScope();

    private IResourceSampler ResourceSampler => Scope.ServiceProvider.GetRequiredService<IResourceSampler>();

    private IProcessMonitor ProcessMonitor => Scope.ServiceProvider.GetRequiredService<IProcessMonitor>();

    private RunRegistry RunRegistry => Scope.ServiceProvider.GetRequiredService<RunRegistry>();

    [Given(@"a dotnet run is in flight against a long-running fixture")]
    public async Task GivenADotnetRunIsInFlightAgainstALongRunningFixture()
    {
        _inFlightClient = Factory.CreateClient();
        var result = await McpTestClient.CallToolAsync(
            _inFlightClient, "dotnet_run", new { arguments = new[] { "test" }, workingDirectory = "HangingFixture" }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        Assert.False(result.IsToolError, result.ContentText);
        _runId = JsonDocument.Parse(result.ContentText!).RootElement.GetProperty("runId").GetGuid();

        // dotnet_run returns once the run is accepted (lock acquired) but before the spawned
        // dotnet.exe process id is necessarily recorded - poll until RunRegistry actually has it,
        // rather than assuming.
        for (var i = 0; i < 100 && !RunRegistry.GetTrackedProcessRoots().Any(r => r.RunId == _runId); i++)
        {
            await Task.Delay(50, CancellationToken.None);
        }
    }

    [When(@"the resource sampler ticks")]
    [Given(@"the resource sampler ticks")]
    public async Task WhenTheResourceSamplerTicks() => _snapshot = await ResourceSampler.SampleAsync(CancellationToken.None);

    [Then(@"that run's process sublist includes its spawned process with plausible CPU and RAM values")]
    public void ThenThatRunSProcessSublistIncludesItsSpawnedProcessWithPlausibleCpuAndRamValues()
    {
        var runAggregate = Assert.Single(_snapshot!.Runs, r => r.RunId == _runId);

        Assert.NotEmpty(runAggregate.Processes);
        Assert.Contains(runAggregate.Processes, p => p.ProcessName.Contains("dotnet", StringComparison.OrdinalIgnoreCase));
        Assert.All(runAggregate.Processes, p =>
        {
            Assert.InRange(p.CpuPercent, 0, 100);
            Assert.True(p.RamBytes > 0, $"Expected {p.ProcessName} to report positive RAM usage.");
        });
    }

    [When(@"the operator kills that run's root process")]
    public async Task WhenTheOperatorKillsThatRunSRootProcess()
    {
        var runAggregate = Assert.Single(_snapshot!.Runs, r => r.RunId == _runId);
        var rootProcessId = RunRegistry.GetTrackedProcessRoots().Single(r => r.RunId == _runId).ProcessId;
        var rootProcess = Assert.Single(runAggregate.Processes, p => p.ProcessId == rootProcessId);

        _killAccepted = await ProcessMonitor.KillAsync(rootProcess.ProcessId, rootProcess.StartTime, CancellationToken.None);
    }

    [Then(@"the kill is accepted")]
    public void ThenTheKillIsAccepted() => Assert.True(_killAccepted);

    [Then(@"the run's process sublist is empty on the next tick")]
    public async Task ThenTheRunSProcessSublistIsEmptyOnTheNextTick()
    {
        // The kill causes the spawned dotnet.exe to exit, which the SSE handler observes and
        // reacts to (releasing the RunRegistry entry) on its own schedule - poll rather than
        // assume it's already released by the time this step runs.
        for (var i = 0; i < 100 && RunRegistry.GetTrackedProcessRoots().Any(r => r.RunId == _runId); i++)
        {
            await Task.Delay(50, CancellationToken.None);
        }

        var snapshot = await ResourceSampler.SampleAsync(CancellationToken.None);
        Assert.DoesNotContain(snapshot.Runs, r => r.RunId == _runId);
    }

    [When(@"the resource sampler ticks with nothing in flight")]
    public async Task WhenTheResourceSamplerTicksWithNothingInFlight() => _snapshot = await ResourceSampler.SampleAsync(CancellationToken.None);

    [Then(@"no run has a process sublist")]
    public void ThenNoRunHasAProcessSublist() => Assert.Empty(_snapshot!.Runs);

    [Given(@"a transfer is registered as in flight")]
    public void GivenATransferIsRegisteredAsInFlight()
    {
        _runId = Guid.NewGuid();
        RunRegistry.RegisterTransfer(_runId, new CancellationTokenSource());
    }

    [Then(@"the transfer has no process sublist")]
    public void ThenTheTransferHasNoProcessSublist() => Assert.DoesNotContain(_snapshot!.Runs, r => r.RunId == _runId);

    /// <inheritdoc />
    public void Dispose()
    {
        // Best-effort: if a scenario didn't already kill the hung fixture's process, cancel it
        // directly via the registry (in-process, fast) rather than leaving it running - the same
        // lesson §13 already applied to this file's sibling steps classes.
        if (_scope is not null)
        {
            RunRegistry.TryCancel(_runId);
        }

        _inFlightClient?.Dispose();
        _scope?.Dispose();
        _factory?.Dispose();
    }
}
