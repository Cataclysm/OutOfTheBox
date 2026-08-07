// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Monitoring;

namespace OutOfTheBox.UnitTests.Domain.Monitoring;

public sealed class ResourceAggregationTests
{
    [Fact]
    public void Sum_adds_cpu_and_ram_across_every_sample()
    {
        var samples = new[] { (CpuPercent: 10.0, RamBytes: 100L), (CpuPercent: 25.5, RamBytes: 200L), (CpuPercent: 4.5, RamBytes: 300L) };

        var (cpuPercent, ramBytes) = ResourceAggregation.Sum(samples);

        Assert.Equal(40.0, cpuPercent);
        Assert.Equal(600L, ramBytes);
    }

    [Fact]
    public void Sum_of_no_samples_is_zero()
    {
        var (cpuPercent, ramBytes) = ResourceAggregation.Sum([]);

        Assert.Equal(0, cpuPercent);
        Assert.Equal(0, ramBytes);
    }

    [Fact]
    public void CpuPercentFromDelta_computes_the_standard_formula()
    {
        // 500ms of processor time consumed over a 1000ms wall-clock window, on a single-core
        // machine, is 50% - the textbook per-process CPU% delta-sampling case.
        var cpuPercent = ResourceAggregation.CpuPercentFromDelta(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1000),
            processorCount: 1);

        Assert.Equal(50.0, cpuPercent, precision: 5);
    }

    [Fact]
    public void CpuPercentFromDelta_divides_by_processor_count()
    {
        // The same 500ms of processor time over the same 1000ms window, but spread across 4 cores,
        // is 12.5% of total capacity.
        var cpuPercent = ResourceAggregation.CpuPercentFromDelta(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1000),
            processorCount: 4);

        Assert.Equal(12.5, cpuPercent, precision: 5);
    }

    [Fact]
    public void CpuPercentFromDelta_returns_zero_for_a_non_positive_wall_clock_delta()
    {
        // The first-ever sample for a process (nothing to diff against yet) has no meaningful
        // prior wall-clock point - must not divide by zero or go negative.
        Assert.Equal(0, ResourceAggregation.CpuPercentFromDelta(TimeSpan.FromMilliseconds(100), TimeSpan.Zero, 1));
        Assert.Equal(0, ResourceAggregation.CpuPercentFromDelta(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(-5), 1));
    }
}
