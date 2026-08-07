// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Monitoring;

/// <summary>
/// Pure CPU%/RAM aggregation math, per design.md's Architecture section ("Domain... pure business
/// rules... CPU%/RAM aggregation math"). Per design.md's "Per-run aggregation" decision: a run's
/// aggregate figures are just the sum of its already-sampled process tree's per-process figures at
/// the same tick - no separate sampling mechanism.
/// </summary>
public static class ResourceAggregation
{
    /// <summary>Sums CPU%/RAM across a set of already-sampled processes (e.g. one run's process tree at one tick).</summary>
    public static (double CpuPercent, long RamBytes) Sum(IEnumerable<(double CpuPercent, long RamBytes)> processSamples)
    {
        double cpuPercent = 0;
        long ramBytes = 0;

        foreach (var (processCpuPercent, processRamBytes) in processSamples)
        {
            cpuPercent += processCpuPercent;
            ramBytes += processRamBytes;
        }

        return (cpuPercent, ramBytes);
    }

    /// <summary>
    /// Per-process CPU% via delta-sampling between two ticks, per design.md's "Per-process CPU%"
    /// decision: <c>(ΔTotalProcessorTime / (Δwallclock × processorCount)) × 100</c>. Returns 0 for a
    /// non-positive wall-clock delta (e.g. the process's very first sample, with nothing to diff
    /// against yet) rather than dividing by zero or a negative span.
    /// </summary>
    public static double CpuPercentFromDelta(TimeSpan processorTimeDelta, TimeSpan wallClockDelta, int processorCount)
    {
        if (wallClockDelta <= TimeSpan.Zero || processorCount <= 0)
        {
            return 0;
        }

        return processorTimeDelta.TotalMilliseconds / (wallClockDelta.TotalMilliseconds * processorCount) * 100;
    }
}
