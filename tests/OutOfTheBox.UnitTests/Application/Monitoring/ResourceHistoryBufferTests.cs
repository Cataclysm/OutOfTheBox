// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Monitoring;

namespace OutOfTheBox.UnitTests.Application.Monitoring;

public sealed class ResourceHistoryBufferTests
{
    [Fact]
    public void Add_then_Get_returns_the_point_in_order()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var buffer = new ResourceHistoryBuffer(clock);

        buffer.Add("run-a", clock.UtcNow, 10, 100);
        clock.Advance(TimeSpan.FromSeconds(1));
        buffer.Add("run-a", clock.UtcNow, 20, 200);

        var points = buffer.Get("run-a");

        Assert.Equal(2, points.Count);
        Assert.Equal(10, points[0].CpuPercent);
        Assert.Equal(20, points[1].CpuPercent);
    }

    [Fact]
    public void Get_returns_empty_for_an_unknown_series()
    {
        var buffer = new ResourceHistoryBuffer(new FakeClock(DateTimeOffset.UtcNow));

        Assert.Empty(buffer.Get("never-added"));
    }

    [Fact]
    public void Series_are_independent()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var buffer = new ResourceHistoryBuffer(clock);

        buffer.Add(ResourceHistoryBuffer.HostSeriesKey, clock.UtcNow, 5, 50);
        buffer.Add("run-a", clock.UtcNow, 15, 150);

        Assert.Single(buffer.Get(ResourceHistoryBuffer.HostSeriesKey));
        Assert.Single(buffer.Get("run-a"));
    }

    [Fact]
    public void Points_older_than_20_minutes_are_evicted_on_the_next_add()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var buffer = new ResourceHistoryBuffer(clock);

        buffer.Add("run-a", clock.UtcNow, 1, 10);

        clock.Advance(TimeSpan.FromMinutes(19));
        buffer.Add("run-a", clock.UtcNow, 2, 20);
        Assert.Equal(2, buffer.Get("run-a").Count); // still within the 20-minute window

        clock.Advance(TimeSpan.FromMinutes(2));
        buffer.Add("run-a", clock.UtcNow, 3, 30);

        var points = buffer.Get("run-a");
        Assert.Equal(2, points.Count); // the first point (21 minutes old) has aged out
        Assert.DoesNotContain(points, p => p.CpuPercent == 1);
    }

    [Fact]
    public void Get_with_a_window_further_restricts_to_only_recent_points()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var buffer = new ResourceHistoryBuffer(clock);

        buffer.Add("run-a", clock.UtcNow, 1, 10);
        clock.Advance(TimeSpan.FromMinutes(15));
        buffer.Add("run-a", clock.UtcNow, 2, 20);

        // Both points are still within the buffer's own 20-minute retention...
        Assert.Equal(2, buffer.Get("run-a").Count);

        // ...but a 10-minute window excludes the first (15 minutes old at this point) without
        // evicting it from the buffer itself - a second, unwindowed Get() still sees both.
        var windowed = buffer.Get("run-a", TimeSpan.FromMinutes(10));
        var single = Assert.Single(windowed);
        Assert.Equal(2, single.CpuPercent);
        Assert.Equal(2, buffer.Get("run-a").Count);
    }

    [Fact]
    public void Add_round_trips_the_host_only_per_core_network_and_disk_figures()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var buffer = new ResourceHistoryBuffer(clock);

        buffer.Add(ResourceHistoryBuffer.HostSeriesKey, clock.UtcNow, 10, 100, [10.0, 20.0], 500, 1500, 700, 300);
        buffer.Add("run-a", clock.UtcNow, 5, 50);

        var hostPoint = Assert.Single(buffer.Get(ResourceHistoryBuffer.HostSeriesKey));
        Assert.Equal([10.0, 20.0], hostPoint.PerCoreCpuPercent);
        Assert.Equal(500, hostPoint.NetworkBytesSentPerSecond);
        Assert.Equal(1500, hostPoint.NetworkBytesReceivedPerSecond);
        Assert.Equal(700, hostPoint.DiskReadBytesPerSecond);
        Assert.Equal(300, hostPoint.DiskWriteBytesPerSecond);

        var runPoint = Assert.Single(buffer.Get("run-a"));
        Assert.Null(runPoint.PerCoreCpuPercent);
        Assert.Null(runPoint.NetworkBytesSentPerSecond);
        Assert.Null(runPoint.NetworkBytesReceivedPerSecond);
        Assert.Null(runPoint.DiskReadBytesPerSecond);
        Assert.Null(runPoint.DiskWriteBytesPerSecond);
    }

    [Fact]
    public void Remove_clears_a_runs_series()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var buffer = new ResourceHistoryBuffer(clock);
        buffer.Add("run-a", clock.UtcNow, 1, 10);

        buffer.Remove("run-a");

        Assert.Empty(buffer.Get("run-a"));
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
