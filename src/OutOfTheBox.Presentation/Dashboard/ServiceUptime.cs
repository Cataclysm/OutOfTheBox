// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Diagnostics;
using System.Globalization;

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>
/// Reads the current process's own start time once, via the same <c>Process.GetCurrentProcess()</c>
/// call the host resource sampler already uses for Service RAM - the single place the Status page's
/// uptime tile reads from, rather than each caller querying the process directly.
/// </summary>
public static class ServiceUptime
{
    /// <summary>The moment this process started, in UTC.</summary>
    public static DateTimeOffset StartedAt { get; } = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>Formats the time elapsed since <see cref="StartedAt"/> as "Xd Yh Zm" once at least a day has passed, or "h:mm:ss" below that.</summary>
    public static string Format(DateTimeOffset utcNow)
    {
        var uptime = utcNow - StartedAt;

        return uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m"
            : uptime.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
