// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Monitoring;
using OutOfTheBox.Infrastructure.Monitoring;

namespace OutOfTheBox.UnitTests.Infrastructure.Monitoring;

/// <summary>
/// Exercises the real <see cref="HostResourceSampler"/> - real <c>PerformanceCounter</c>/
/// <c>GlobalMemoryStatusEx</c>/WMI calls, not fakes, since this is what actually proves the
/// counters are wired correctly on this OS (host CPU%/RAM values are inherently non-deterministic
/// real system state, so assertions check plausible ranges and internal consistency, not exact
/// figures). This isn't "real process spawning" in the sense this project's UnitTests convention
/// warns against - no child process is started, only OS counters are read.
/// </summary>
public sealed class HostResourceSamplerTests : IDisposable
{
    private readonly HostResourceSampler _sampler;

    public HostResourceSamplerTests()
    {
        _sampler = new HostResourceSampler(new RunRegistry(), new SystemClock());
    }

    [Fact]
    public async Task SampleAsync_returns_plausible_host_figures()
    {
        var snapshot = await _sampler.SampleAsync(CancellationToken.None);

        Assert.InRange(snapshot.Host.TotalCpuPercent, 0, 100);
        Assert.Equal(Environment.ProcessorCount, snapshot.Host.PerCoreCpuPercent.Count);
        Assert.All(snapshot.Host.PerCoreCpuPercent, core => Assert.InRange(core, 0, 100));
        Assert.True(snapshot.Host.TotalRamBytes > 0);
        Assert.InRange(snapshot.Host.AvailableRamBytes, 0, snapshot.Host.TotalRamBytes);
        Assert.True(snapshot.Host.ServiceRamBytes > 0);
    }

    [Fact]
    public async Task SampleAsync_reports_no_tracked_runs_when_the_registry_is_empty()
    {
        var snapshot = await _sampler.SampleAsync(CancellationToken.None);

        Assert.Empty(snapshot.Runs);
    }

    [Fact]
    public async Task SampleAsync_can_be_called_repeatedly_on_the_configured_interval()
    {
        // Simulates the background sampler's own repeated-tick behavior (task 14.10) - the
        // interval itself is a real (short) delay here since PerformanceCounter reads real system
        // state that can't be driven by a fake clock; what's being verified is that consecutive
        // ticks keep succeeding and returning internally consistent data, not exact values.
        var first = await _sampler.SampleAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        var second = await _sampler.SampleAsync(CancellationToken.None);

        Assert.True(second.Timestamp > first.Timestamp);
        Assert.InRange(second.Host.TotalCpuPercent, 0, 100);
    }

    /// <inheritdoc />
    public void Dispose() => _sampler.Dispose();
}
