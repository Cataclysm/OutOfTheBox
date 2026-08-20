// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Collections.Concurrent;
using OutOfTheBox.Application.Events;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// Process-wide map of run id to that run's <see cref="McpRunOutputBuffer"/> - registered as a
/// singleton (the same "process-wide, in-memory" pattern <see cref="OutOfTheBox.Application.Concurrency.RunRegistry"/>
/// already uses), created once when <c>dotnet_run</c>/<c>git_run</c>/<c>clone_repository</c> starts
/// a run and read by <c>read_run_output</c> for as long as the buffer is retained. Self-evicting: a
/// buffer is dropped <see cref="RetentionAfterTerminal"/> after its run's <see cref="RunEventType.Terminal"/>
/// event, not immediately - <c>read_run_output</c>'s own contract ("a run that has already reached a
/// terminal status SHALL continue to return ... repeatably") means a caller polling shortly after
/// seeing terminal status must still get the final chunk, so only *unbounded* retention was ever the
/// actual problem (observed as multi-GB service working-set growth over the service's uptime, with
/// no cap on run count).
/// </summary>
public sealed class McpRunOutputRegistry : IDisposable
{
    private static readonly TimeSpan RetentionAfterTerminal = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, McpRunOutputBuffer> _buffers = new();
    private readonly IDisposable _subscription;

    /// <summary>Subscribes to <paramref name="runEventBus"/> for terminal-event-driven eviction (see class remarks).</summary>
    public McpRunOutputRegistry(IRunEventBus runEventBus) => _subscription = runEventBus.Subscribe(OnRunEvent);

    /// <summary>Creates and registers a new buffer for <paramref name="runId"/>, capped at <paramref name="capBytes"/>.</summary>
    public McpRunOutputBuffer Create(Guid runId, long capBytes)
    {
        var buffer = new McpRunOutputBuffer(capBytes);
        _buffers[runId] = buffer;
        return buffer;
    }

    /// <summary>Looks up the buffer for <paramref name="runId"/>, or <see langword="null"/> if none was ever created for it.</summary>
    public McpRunOutputBuffer? TryGet(Guid runId) => _buffers.GetValueOrDefault(runId);

    private void OnRunEvent(RunEvent runEvent)
    {
        if (runEvent.Type != RunEventType.Terminal)
        {
            return;
        }

        // A no-op TryRemove for a run this registry never created a buffer for (a dashboard-only
        // run, or one of the other MCP run kinds with no output buffer) - Terminal fires for every
        // run kind, not just the three that call Create above.
        _ = Task.Delay(RetentionAfterTerminal).ContinueWith(
            completedDelay => _buffers.TryRemove(runEvent.RunId, out _),
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public void Dispose() => _subscription.Dispose();
}
