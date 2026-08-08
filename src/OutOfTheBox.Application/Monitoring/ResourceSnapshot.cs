// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Monitoring;

/// <summary>One process's figures at a single sampler tick.</summary>
public sealed record ProcessResourceSample(int ProcessId, string ProcessName, DateTime StartTime, double CpuPercent, long RamBytes);

/// <summary>
/// One <c>dotnet</c>/<c>git</c>/clone run's aggregate figures at a single tick - the sum of its
/// process tree's per-process figures, per design.md's "Per-run aggregation" decision.
/// </summary>
public sealed record RunResourceAggregate(Guid RunId, double CpuPercent, long RamBytes, IReadOnlyList<ProcessResourceSample> Processes);

/// <summary>Host-wide CPU/RAM/network figures at a single tick. Network figures are already-per-second rates (summed across every network interface), not cumulative counters.</summary>
public sealed record HostResourceSample(
    double TotalCpuPercent,
    IReadOnlyList<double> PerCoreCpuPercent,
    long TotalRamBytes,
    long AvailableRamBytes,
    long ServiceRamBytes,
    double NetworkBytesSentPerSecond,
    double NetworkBytesReceivedPerSecond);

/// <summary>Everything sampled in one tick: host figures plus every currently-tracked run's aggregate.</summary>
public sealed record ResourceSnapshot(DateTimeOffset Timestamp, HostResourceSample Host, IReadOnlyList<RunResourceAggregate> Runs);
