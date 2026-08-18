// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>The immediate result of starting a run (<c>dotnet_run</c>/<c>git_run</c>/<c>clone_repository</c>) - returned before the run finishes, per those tools' start-then-poll contract.</summary>
/// <param name="RunId">The started run's id - pass this to <c>read_run_output</c>/<c>cancel_run</c>.</param>
/// <param name="Status">Always <see cref="McpRunStatus.Running"/> at the moment a run is accepted.</param>
public sealed record McpStartRunResult(Guid RunId, string Status);

/// <summary>One <c>read_run_output</c> result: output produced since the requested offset, plus the run's current status.</summary>
/// <param name="Status">One of <see cref="McpRunStatus"/>'s values.</param>
/// <param name="Stdout">Standard output produced since the requested offset (empty if none).</param>
/// <param name="Stderr">Standard error produced since the requested offset (empty if none).</param>
/// <param name="NextOffset">Pass this back on the next <c>read_run_output</c> call to continue reading from here.</param>
/// <param name="Truncated">Whether the run's combined output has exceeded the configured cap.</param>
/// <param name="ExitCode">The process exit code, once <paramref name="Status"/> is <see cref="McpRunStatus.Completed"/>; <see langword="null"/> otherwise.</param>
public sealed record McpReadRunOutputResult(string Status, string Stdout, string Stderr, long NextOffset, bool Truncated, int? ExitCode);

/// <summary>The result of a <c>cancel_run</c> call - the run's status immediately after the cancellation was requested (may still read "running" until the process actually exits; poll <c>read_run_output</c> to observe the terminal transition).</summary>
/// <param name="Status">One of <see cref="McpRunStatus"/>'s values.</param>
public sealed record McpCancelRunResult(string Status);

/// <summary>One resource-usage sample point for a run, per <c>get_run_resources</c>.</summary>
/// <param name="Timestamp">When this sample was taken.</param>
/// <param name="CpuPercent">The run's aggregate process-tree CPU utilization at this tick, as a percentage.</param>
/// <param name="RamBytes">The run's aggregate resident memory at this tick, in bytes.</param>
public sealed record McpRunResourcePoint(DateTimeOffset Timestamp, double CpuPercent, long RamBytes);

/// <summary>A derived "is this run still doing something" summary over <c>get_run_resources</c>'s returned points - see that tool's own remarks for the idle-threshold rule.</summary>
/// <param name="LatestCpuPercent">The most recent sample's CPU percentage.</param>
/// <param name="PeakCpuPercent">The highest CPU percentage observed anywhere in the returned window.</param>
/// <param name="IdleForSeconds">How long it's been since CPU last exceeded the idle threshold.</param>
public sealed record McpRunResourceTrend(double LatestCpuPercent, double PeakCpuPercent, double IdleForSeconds);

/// <summary>The result of a <c>get_run_resources</c> call.</summary>
/// <param name="Points">The run's recent resource-usage samples, oldest first - empty if none have been taken yet (a run just started, or one with nothing left in the trailing window).</param>
/// <param name="Trend">The derived hung-vs-busy summary, or <see langword="null"/> when <paramref name="Points"/> is empty.</param>
public sealed record McpGetRunResourcesResult(IReadOnlyList<McpRunResourcePoint> Points, McpRunResourceTrend? Trend);
