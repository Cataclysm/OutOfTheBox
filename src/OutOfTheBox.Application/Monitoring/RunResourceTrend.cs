// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Monitoring;

/// <summary>
/// A run's resource-usage trend, derived from its <see cref="ResourceHistoryBuffer"/> series - lets
/// an MCP caller judge "hung vs. busy" from three numbers instead of interpreting raw points itself.
/// </summary>
/// <param name="LatestCpuPercent">The most recent sample's CPU percentage.</param>
/// <param name="PeakCpuPercent">The highest CPU percentage observed anywhere in the returned window.</param>
/// <param name="IdleForSeconds">
/// How long it has been since CPU last exceeded <see cref="RunResourceTrend.IdleCpuPercentThreshold"/>,
/// measured from that last above-threshold point - or, if every point in the window is at or below
/// the threshold, from the window's own earliest point (the true idle start may predate what's
/// visible; this reports a lower bound, not a guess).
/// </param>
public sealed record RunResourceTrendSummary(double LatestCpuPercent, double PeakCpuPercent, double IdleForSeconds);

/// <summary>
/// Pure computation over a <see cref="ResourceHistoryBuffer"/> series - see <see cref="RunResourceTrendSummary"/>.
/// No IO, no DI: takes "now" as a plain parameter so it stays trivially unit-testable, the same
/// shape as <c>OutOfTheBox.Domain.Repositories.CommitGraphLayout</c>'s pure graph computation. Lives
/// here rather than in Domain because its input type, <see cref="ResourceHistoryPoint"/>, already
/// lives in Application (Domain cannot reference Application).
/// </summary>
public static class RunResourceTrend
{
    // A run doing real, sustained work rarely idles all the way down to exactly 0% between sampler
    // ticks (background OS/runtime activity alone tends to register a sliver of CPU) - a couple of
    // percent gives real idling room to read as idle without a run doing genuinely light but real
    // work (e.g. a slow, mostly network/disk-bound restore) getting misclassified from one noisy
    // near-zero tick alone. Inclusive at the boundary: a point at exactly the threshold counts as
    // idle, not active.
    private const double IdleCpuPercentThreshold = 2.0;

    /// <summary>
    /// Computes the trend summary for <paramref name="points"/> as of <paramref name="now"/>, or
    /// <see langword="null"/> if there are no points yet (a run just started, or one this service
    /// has never sampled).
    /// </summary>
    public static RunResourceTrendSummary? Compute(IReadOnlyList<ResourceHistoryPoint> points, DateTimeOffset now)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var peakCpuPercent = points.Max(p => p.CpuPercent);

        DateTimeOffset? lastActiveTimestamp = null;
        foreach (var point in points)
        {
            if (point.CpuPercent > IdleCpuPercentThreshold)
            {
                lastActiveTimestamp = point.Timestamp;
            }
        }

        var idleSinceTimestamp = lastActiveTimestamp ?? points[0].Timestamp;
        var idleForSeconds = Math.Max(0, (now - idleSinceTimestamp).TotalSeconds);

        return new RunResourceTrendSummary(points[^1].CpuPercent, peakCpuPercent, idleForSeconds);
    }
}
