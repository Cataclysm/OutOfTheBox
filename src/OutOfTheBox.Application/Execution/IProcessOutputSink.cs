// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Execution;

/// <summary>
/// Receives a running process's stdout/stderr lines as they're produced, so a caller (an MCP tool's
/// output buffer) can forward each one immediately rather than waiting for the process to exit.
/// </summary>
public interface IProcessOutputSink
{
    /// <summary>Called for each line of stdout, in order, as it's produced.</summary>
    Task OnStandardOutputAsync(string line, CancellationToken cancellationToken);

    /// <summary>Called for each line of stderr, in order, as it's produced.</summary>
    Task OnStandardErrorAsync(string line, CancellationToken cancellationToken);
}
