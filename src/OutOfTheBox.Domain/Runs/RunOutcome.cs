// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Runs;

/// <summary>The terminal (or in-flight) state of a run.</summary>
public enum RunOutcome
{
    /// <summary>The run's process is still executing.</summary>
    Running,

    /// <summary>The process exited on its own; see the run's exit code for success/failure.</summary>
    Completed,

    /// <summary>The process was killed because it exceeded its execution timeout.</summary>
    TimedOut,

    /// <summary>The process was killed because the caller cancelled the run.</summary>
    Cancelled,

    /// <summary>The request failed validation (path confinement, missing arguments, etc.) and no process was started.</summary>
    ValidationFailed,

    /// <summary>The run was still in flight when the service restarted, so its true outcome is unknown.</summary>
    Interrupted,
}
