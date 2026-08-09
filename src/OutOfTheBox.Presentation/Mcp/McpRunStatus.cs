// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Runs;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// Maps a persisted <see cref="RunOutcome"/> onto the closed status vocabulary
/// <c>mcp-command-execution</c>'s spec defines for <c>read_run_output</c>/<c>cancel_run</c>
/// ("running", "completed", "timed out", "cancelled", or "failed-to-start"), shared by every MCP
/// tool that reports a run's status so the mapping is defined in exactly one place.
/// </summary>
internal static class McpRunStatus
{
    /// <summary>The run's process is still executing.</summary>
    public const string Running = "running";

    /// <summary>The process exited on its own.</summary>
    public const string Completed = "completed";

    /// <summary>The process was killed for exceeding its execution timeout.</summary>
    public const string TimedOut = "timed out";

    /// <summary>The run was cancelled (explicitly, or because the service restarted while it was in flight).</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The executable failed to even start.</summary>
    public const string FailedToStart = "failed-to-start";

    /// <summary>Maps <paramref name="outcome"/> onto this vocabulary.</summary>
    public static string FromOutcome(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Running => Running,
        RunOutcome.Completed => Completed,
        RunOutcome.TimedOut => TimedOut,
        RunOutcome.Cancelled => Cancelled,
        RunOutcome.Failed => FailedToStart,
        // The service restarted while this run was still in flight (per IRunRepository's
        // reconciliation) - not in the spec's vocabulary verbatim, but "cancelled" is the closest
        // fit: the run was definitely ended, just not through this capability's own cancel_run call.
        RunOutcome.Interrupted => Cancelled,
        // ValidationFailed/NotFound/AlreadyExists are never persisted for a run this capability
        // starts (a validation failure throws before any Run row is created) - unreachable in
        // practice, but the switch must stay exhaustive over the full enum.
        _ => Cancelled,
    };
}
