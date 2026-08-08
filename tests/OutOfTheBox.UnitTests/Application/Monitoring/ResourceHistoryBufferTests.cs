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
    public void Points_older_than_10_minutes_are_evicted_on_the_next_add()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var buffer = new ResourceHistoryBuffer(clock);

        buffer.Add("run-a", clock.UtcNow, 1, 10);

        clock.Advance(TimeSpan.FromMinutes(9));
        buffer.Add("run-a", clock.UtcNow, 2, 20);
        Assert.Equal(2, buffer.Get("run-a").Count); // still within the 10-minute window

        clock.Advance(TimeSpan.FromMinutes(2));
        buffer.Add("run-a", clock.UtcNow, 3, 30);

        var points = buffer.Get("run-a");
        Assert.Equal(2, points.Count); // the first point (11 minutes old) has aged out
        Assert.DoesNotContain(points, p => p.CpuPercent == 1);
    }

    [Fact]
    public void Add_round_trips_the_host_only_per_core_and_network_figures()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var buffer = new ResourceHistoryBuffer(clock);

        buffer.Add(ResourceHistoryBuffer.HostSeriesKey, clock.UtcNow, 10, 100, [10.0, 20.0], 500, 1500);
        buffer.Add("run-a", clock.UtcNow, 5, 50);

        var hostPoint = Assert.Single(buffer.Get(ResourceHistoryBuffer.HostSeriesKey));
        Assert.Equal([10.0, 20.0], hostPoint.PerCoreCpuPercent);
        Assert.Equal(500, hostPoint.NetworkBytesSentPerSecond);
        Assert.Equal(1500, hostPoint.NetworkBytesReceivedPerSecond);

        var runPoint = Assert.Single(buffer.Get("run-a"));
        Assert.Null(runPoint.PerCoreCpuPercent);
        Assert.Null(runPoint.NetworkBytesSentPerSecond);
        Assert.Null(runPoint.NetworkBytesReceivedPerSecond);
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
