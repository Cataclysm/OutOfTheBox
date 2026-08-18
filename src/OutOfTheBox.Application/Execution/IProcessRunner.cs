// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Execution;

/// <summary>
/// Runs the <c>dotnet</c> CLI as a child process, forwarding its output to
/// <see cref="IProcessOutputSink"/> as it's produced. Cancelling the run's cancellation token
/// (via a caller-triggered cancel or an execution timeout) kills the process tree and the call
/// throws <see cref="OperationCanceledException"/>; a process that exits on its own (any exit
/// code) instead returns normally.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs the process described by <paramref name="request"/> to completion or cancellation.
    /// <paramref name="onStarted"/>, if supplied, is invoked once with the spawned process's id as
    /// soon as it starts (per specs/host-resource-monitoring - the caller records it, typically
    /// into <see cref="Concurrency.RunRegistry"/>, as the root to walk a process tree from).
    /// </summary>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken, Action<int>? onStarted = null);
}
