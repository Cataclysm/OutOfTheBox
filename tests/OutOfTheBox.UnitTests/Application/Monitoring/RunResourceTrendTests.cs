// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Monitoring;

namespace OutOfTheBox.UnitTests.Application.Monitoring;

public sealed class RunResourceTrendTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_returns_null_for_an_empty_series() => Assert.Null(RunResourceTrend.Compute([], Start));

    [Fact]
    public void Compute_reflects_a_single_point()
    {
        var points = new[] { Point(Start, cpuPercent: 42) };

        var summary = RunResourceTrend.Compute(points, Start);

        Assert.NotNull(summary);
        Assert.Equal(42, summary.LatestCpuPercent);
        Assert.Equal(42, summary.PeakCpuPercent);
        Assert.Equal(0, summary.IdleForSeconds);
    }

    [Fact]
    public void Compute_reports_idle_since_the_windows_start_when_every_point_is_at_or_below_the_threshold()
    {
        var points = new[]
        {
            Point(Start, cpuPercent: 0),
            Point(Start.AddSeconds(30), cpuPercent: 1),
            Point(Start.AddSeconds(60), cpuPercent: 0),
        };
        var now = Start.AddSeconds(90);

        var summary = RunResourceTrend.Compute(points, now);

        Assert.NotNull(summary);
        Assert.Equal(0, summary.LatestCpuPercent);
        Assert.Equal(1, summary.PeakCpuPercent);
        Assert.Equal(90, summary.IdleForSeconds); // measured from the earliest visible point, not "now" minus 0
    }

    [Fact]
    public void Compute_reports_near_zero_idle_time_when_the_most_recent_point_is_active()
    {
        var points = new[]
        {
            Point(Start, cpuPercent: 0),
            Point(Start.AddSeconds(30), cpuPercent: 85),
        };
        var now = Start.AddSeconds(30);

        var summary = RunResourceTrend.Compute(points, now);

        Assert.NotNull(summary);
        Assert.Equal(85, summary.LatestCpuPercent);
        Assert.Equal(85, summary.PeakCpuPercent);
        Assert.Equal(0, summary.IdleForSeconds);
    }

    [Fact]
    public void Compute_treats_a_point_exactly_at_the_idle_threshold_as_idle()
    {
        // 2.0% is the idle threshold - a point sitting exactly on it counts as idle (inclusive),
        // not active, so it must not reset the idle-since timestamp.
        var points = new[]
        {
            Point(Start, cpuPercent: 0),
            Point(Start.AddSeconds(60), cpuPercent: 2.0),
        };
        var now = Start.AddSeconds(60);

        var summary = RunResourceTrend.Compute(points, now);

        Assert.NotNull(summary);
        Assert.Equal(60, summary.IdleForSeconds);
    }

    [Fact]
    public void Compute_measures_idle_time_from_the_busy_to_idle_transition_not_the_windows_start()
    {
        var points = new[]
        {
            Point(Start, cpuPercent: 90), // busy at the start of the window
            Point(Start.AddSeconds(60), cpuPercent: 75), // still busy
            Point(Start.AddSeconds(120), cpuPercent: 0), // goes idle here
            Point(Start.AddSeconds(180), cpuPercent: 0),
        };
        var now = Start.AddSeconds(240);

        var summary = RunResourceTrend.Compute(points, now);

        Assert.NotNull(summary);
        Assert.Equal(0, summary.LatestCpuPercent);
        Assert.Equal(90, summary.PeakCpuPercent);
        Assert.Equal(180, summary.IdleForSeconds); // now (240) minus the *last active* point (60), not minus the window start (0)
    }

    private static ResourceHistoryPoint Point(DateTimeOffset timestamp, double cpuPercent) =>
        new(timestamp, cpuPercent, RamBytes: 0);
}
