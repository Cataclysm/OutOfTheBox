// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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
    private readonly PerformanceCounter[] _networkSentCounters;
    private readonly PerformanceCounter[] _networkReceivedCounters;
    private readonly PerformanceCounter _diskReadCounter;
    private readonly PerformanceCounter _diskWriteCounter;
    private readonly ConcurrentDictionary<int, (TimeSpan ProcessorTime, DateTimeOffset Timestamp)> _lastProcessSample = new();

    /// <summary>Constructs the counters and primes them (see the discarded-first-reading remark above).</summary>
    public HostResourceSampler(RunRegistry runRegistry, IClock clock)
    {
        _runRegistry = runRegistry;
        _clock = clock;

        _totalCpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _perCoreCpuCounters = [.. Enumerable.Range(0, Environment.ProcessorCount).Select(core => new PerformanceCounter("Processor", "% Processor Time", core.ToString(CultureInfo.InvariantCulture)))];

        // One pair of counters per network interface instance (there's no "_Total" instance for
        // this category, unlike Processor) - summed across all of them in SampleAsync, since this
        // is host-wide network activity, not a specific-adapter figure.
        var networkInstanceNames = new PerformanceCounterCategory("Network Interface").GetInstanceNames();
        _networkSentCounters = [.. networkInstanceNames.Select(name => new PerformanceCounter("Network Interface", "Bytes Sent/sec", name))];
        _networkReceivedCounters = [.. networkInstanceNames.Select(name => new PerformanceCounter("Network Interface", "Bytes Received/sec", name))];

        // Unlike Network Interface, "PhysicalDisk" does have a "_Total" instance that Windows itself
        // already sums across every physical drive - no per-instance enumeration/summing needed here,
        // matching the "crunch all drives together" requirement directly.
        _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
        _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

        // A freshly-constructed PerformanceCounter's first NextValue() call always returns 0 (no
        // prior sample to diff against) - discard it here so every call from SampleAsync onward
        // returns a real reading. "Bytes Sent/Received per sec" and "Disk Read/Write Bytes/sec"
        // counters have the same needs-a-baseline-reading behavior as "% Processor Time" does.
        _totalCpuCounter.NextValue();
        foreach (var counter in _perCoreCpuCounters)
        {
            counter.NextValue();
        }

        foreach (var counter in _networkSentCounters)
        {
            counter.NextValue();
        }

        foreach (var counter in _networkReceivedCounters)
        {
            counter.NextValue();
        }

        _diskReadCounter.NextValue();
        _diskWriteCounter.NextValue();
    }

    /// <inheritdoc />
    public async Task<ResourceSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        var timestamp = _clock.UtcNow;

        var totalCpuPercent = _totalCpuCounter.NextValue();
        var perCoreCpuPercent = _perCoreCpuCounters.Select(counter => (double)counter.NextValue()).ToList();
        var (totalRamBytes, availableRamBytes) = Win32MemoryStatus.GetMemoryStatus();
        var serviceRamBytes = Process.GetCurrentProcess().WorkingSet64;
        var networkBytesSentPerSecond = _networkSentCounters.Sum(counter => (double)counter.NextValue());
        var networkBytesReceivedPerSecond = _networkReceivedCounters.Sum(counter => (double)counter.NextValue());
        var diskReadBytesPerSecond = (double)_diskReadCounter.NextValue();
        var diskWriteBytesPerSecond = (double)_diskWriteCounter.NextValue();

        var host = new HostResourceSample(
            totalCpuPercent, perCoreCpuPercent, totalRamBytes, availableRamBytes, serviceRamBytes,
            networkBytesSentPerSecond, networkBytesReceivedPerSecond, diskReadBytesPerSecond, diskWriteBytesPerSecond);
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

        foreach (var counter in _networkSentCounters)
        {
            counter.Dispose();
        }

        foreach (var counter in _networkReceivedCounters)
        {
            counter.Dispose();
        }

        _diskReadCounter.Dispose();
        _diskWriteCounter.Dispose();
    }
}
