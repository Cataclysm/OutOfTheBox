// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Monitoring;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <summary>
/// Background sampler ticking every few seconds (default 3s, per
/// <see cref="ServiceOptions.ResourceSamplerIntervalSeconds"/>): samples host + tracked-run figures
/// via <see cref="IResourceSampler"/>, updates the live <see cref="ResourceHistoryBuffer"/>,
/// publishes to <see cref="IResourceEventBus"/>, and persists one <see cref="RunResourceSample"/>
/// row per in-flight run. Distinct from, and runs at a faster cadence than, §13's repository-stats
/// sampler. Per design.md's "Artifact transfers get a resource-sample series too" decision, each
/// tick also tags every in-flight transfer with that tick's host-level figures, since a transfer
/// has no process tree of its own to sample.
/// </summary>
public sealed class HostResourceSamplerService(
    IOptions<ServiceOptions> options,
    IResourceSampler resourceSampler,
    IResourceEventBus resourceEventBus,
    IRunEventBus runEventBus,
    ResourceHistoryBuffer historyBuffer,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var subscription = runEventBus.Subscribe(OnRunEvent);

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.ResourceSamplerIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                await TickAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown (stoppingToken cancelled) - not an error.
        }
    }

    private void OnRunEvent(RunEvent runEvent)
    {
        // A finished run's live buffer shouldn't linger for the rest of the service's lifetime -
        // the persisted RunResourceSamples series (per design.md) is what history reads instead.
        if (runEvent.Type == RunEventType.Terminal)
        {
            historyBuffer.Remove(runEvent.RunId.ToString());
        }
    }

    /// <summary>
    /// Runs exactly one sample-persist-publish cycle. Public (like <c>CliProcessRunner.BuildStartInfo</c>)
    /// so tests can exercise one tick's persistence/host-tagging logic directly, without waiting on
    /// the real <see cref="PeriodicTimer"/>-driven loop <see cref="ExecuteAsync"/> normally drives it with.
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var snapshot = await resourceSampler.SampleAsync(cancellationToken);

        // Used RAM (total - available), not total physical capacity - the latter is a constant
        // for the host and would make both the live graph and a transfer's tagged samples
        // meaningless (a flat line at total RAM forever), not "how busy is the host."
        var hostRamUsedBytes = snapshot.Host.TotalRamBytes - snapshot.Host.AvailableRamBytes;

        historyBuffer.Add(ResourceHistoryBuffer.HostSeriesKey, snapshot.Timestamp, snapshot.Host.TotalCpuPercent, hostRamUsedBytes);
        foreach (var run in snapshot.Runs)
        {
            historyBuffer.Add(run.RunId.ToString(), snapshot.Timestamp, run.CpuPercent, run.RamBytes);
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        var sampleRepository = scope.ServiceProvider.GetRequiredService<IRunResourceSampleRepository>();

        foreach (var run in snapshot.Runs)
        {
            await sampleRepository.AddAsync(
                new RunResourceSample { RunId = run.RunId, Timestamp = snapshot.Timestamp, CpuPercent = run.CpuPercent, RamBytes = run.RamBytes },
                cancellationToken);
        }

        var inFlightTransfers = await runRepository.ListAsync(
            new RunQuery { Kinds = [RunKind.ArtifactTransfer], Outcomes = [RunOutcome.Running] },
            cancellationToken);

        foreach (var transfer in inFlightTransfers)
        {
            historyBuffer.Add(transfer.Id.ToString(), snapshot.Timestamp, snapshot.Host.TotalCpuPercent, hostRamUsedBytes);
            await sampleRepository.AddAsync(
                new RunResourceSample { RunId = transfer.Id, Timestamp = snapshot.Timestamp, CpuPercent = snapshot.Host.TotalCpuPercent, RamBytes = hostRamUsedBytes },
                cancellationToken);
        }

        resourceEventBus.Publish(snapshot);
    }
}
