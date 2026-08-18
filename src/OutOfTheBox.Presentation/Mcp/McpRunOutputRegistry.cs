// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Collections.Concurrent;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// Process-wide map of run id to that run's <see cref="McpRunOutputBuffer"/> - registered as a
/// singleton (the same "process-wide, in-memory" pattern <see cref="OutOfTheBox.Application.Concurrency.RunRegistry"/>
/// already uses), created once when <c>dotnet_run</c>/<c>git_run</c>/<c>clone_repository</c> starts
/// a run and read by <c>read_run_output</c> for as long as the buffer is retained.
/// </summary>
public sealed class McpRunOutputRegistry
{
    private readonly ConcurrentDictionary<Guid, McpRunOutputBuffer> _buffers = new();

    /// <summary>Creates and registers a new buffer for <paramref name="runId"/>, capped at <paramref name="capBytes"/>.</summary>
    public McpRunOutputBuffer Create(Guid runId, long capBytes)
    {
        var buffer = new McpRunOutputBuffer(capBytes);
        _buffers[runId] = buffer;
        return buffer;
    }

    /// <summary>Looks up the buffer for <paramref name="runId"/>, or <see langword="null"/> if none was ever created for it.</summary>
    public McpRunOutputBuffer? TryGet(Guid runId) => _buffers.GetValueOrDefault(runId);
}
