// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Events;

/// <summary>The three moments in a run's lifecycle <see cref="IRunEventBus"/> publishes, per design.md.</summary>
public enum RunEventType
{
    /// <summary>A run was accepted and started (repository lock acquired, or a transfer/delete registered).</summary>
    Started,

    /// <summary>One stdout/stderr line was produced. Only ever published for <c>dotnet</c>/<c>git</c>/clone runs.</summary>
    OutputLine,

    /// <summary>A run reached a terminal state (completed, timed out, cancelled, or failed validation).</summary>
    Terminal,
}
