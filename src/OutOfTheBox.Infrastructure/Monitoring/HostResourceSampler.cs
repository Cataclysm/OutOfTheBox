// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Monitoring;
using OutOfTheBox.Domain.Monitoring;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <inheritdoc cref="IResourceSampler" />
/// <remarks>
/// Stateful (per-process CPU delta-sampling needs the previous tick's <c>TotalProcessorTime</c> per
/// PID, and <see cref="PerformanceCounter"/> itself needs a discarded first reading per design.md's
/// "Host CPU sampling" decision) - registered as a singleton, disposed with the host.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class HostResourceSampler : IResourceSampler, IDisposable
{
    private readonly RunRegistry _runRegistry;
    private readonly IClock _clock;
    private readonly PerformanceCounter _totalCpuCounter;
    private readonly PerformanceCounter[] _perCoreCpuCounters;
    private readonly ConcurrentDictionary<int, (TimeSpan ProcessorTime, DateTimeOffset Timestamp)> _lastProcessSample = new();

    /// <summary>Constructs the counters and primes them (see the discarded-first-reading remark above).</summary>
    public HostResourceSampler(RunRegistry runRegistry, IClock clock)
    {
        _runRegistry = runRegistry;
        _clock = clock;

        _totalCpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _perCoreCpuCounters = [.. Enumerable.Range(0, Environment.ProcessorCount).Select(core => new PerformanceCounter("Processor", "% Processor Time", core.ToString()))];

        // A freshly-constructed PerformanceCounter's first NextValue() call always returns 0 (no
        // prior sample to diff against) - discard it here so every call from SampleAsync onward
        // returns a real reading.
        _totalCpuCounter.NextValue();
        foreach (var counter in _perCoreCpuCounters)
        {
            counter.NextValue();
        }
    }

    /// <inheritdoc />
    public async Task<ResourceSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        var timestamp = _clock.UtcNow;

        var totalCpuPercent = _totalCpuCounter.NextValue();
        var perCoreCpuPercent = _perCoreCpuCounters.Select(counter => (double)counter.NextValue()).ToList();
        var (totalRamBytes, availableRamBytes) = Win32MemoryStatus.GetMemoryStatus();
        var serviceRamBytes = Process.GetCurrentProcess().WorkingSet64;

        var host = new HostResourceSample(totalCpuPercent, perCoreCpuPercent, totalRamBytes, availableRamBytes, serviceRamBytes);
        var runs = await SampleTrackedRunsAsync(timestamp, cancellationToken);

        return new ResourceSnapshot(timestamp, host, runs);
    }

    private async Task<IReadOnlyList<RunResourceAggregate>> SampleTrackedRunsAsync(DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        var trackedRoots = _runRegistry.GetTrackedProcessRoots();
        if (trackedRoots.Count == 0)
        {
            return [];
        }

        var allProcesses = await WmiProcessTree.GetAllProcessesAsync(cancellationToken);
        var descendantsByRoot = WmiProcessTree.DiscoverDescendants(allProcesses, [.. trackedRoots.Select(r => r.ProcessId)]);

        var aggregates = new List<RunResourceAggregate>();

        foreach (var (runId, rootProcessId) in trackedRoots)
        {
            var processIds = descendantsByRoot.GetValueOrDefault(rootProcessId, []);
            var processSamples = new List<ProcessResourceSample>();

            foreach (var processId in processIds)
            {
                var sample = TrySampleProcess(processId, timestamp);
                if (sample is not null)
                {
                    processSamples.Add(sample);
                }
            }

            var (cpuPercent, ramBytes) = ResourceAggregation.Sum(processSamples.Select(p => (p.CpuPercent, p.RamBytes)));
            aggregates.Add(new RunResourceAggregate(runId, cpuPercent, ramBytes, processSamples));
        }

        return aggregates;
    }

    private ProcessResourceSample? TrySampleProcess(int processId, DateTimeOffset timestamp)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var currentProcessorTime = process.TotalProcessorTime;

            var cpuPercent = _lastProcessSample.TryGetValue(processId, out var last)
                ? ResourceAggregation.CpuPercentFromDelta(currentProcessorTime - last.ProcessorTime, timestamp - last.Timestamp, Environment.ProcessorCount)
                : 0;

            _lastProcessSample[processId] = (currentProcessorTime, timestamp);

            return new ProcessResourceSample(processId, process.ProcessName, process.StartTime, cpuPercent, process.WorkingSet64);
        }
        catch (ArgumentException)
        {
            // Exited between WMI discovery and this sample - skip it for this tick.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _totalCpuCounter.Dispose();

        foreach (var counter in _perCoreCpuCounters)
        {
            counter.Dispose();
        }
    }
}
