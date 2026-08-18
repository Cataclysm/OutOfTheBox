// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

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
