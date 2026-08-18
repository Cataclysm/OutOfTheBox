// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Events;

/// <summary>
/// In-process publish/subscribe for run lifecycle notifications, per design.md's "Dashboard: Blazor
/// Server, backed by an in-process run-event bus for live updates" decision. Published to from the
/// same code paths that already acquire the <see cref="Concurrency.RunRegistry"/> lock and write the
/// <c>Runs</c> table, for every run kind. Dashboard components subscribe to know *when* to
/// re-render/re-query - not to receive the run data itself (see <see cref="RunEvent"/>).
/// </summary>
public interface IRunEventBus
{
    /// <summary>Publishes an event to every current subscriber. Never throws on a subscriber's own failure.</summary>
    void Publish(RunEvent runEvent);

    /// <summary>
    /// Registers <paramref name="handler"/> to be invoked for every subsequently published event.
    /// Dispose the returned handle to unsubscribe - typically from a Blazor component's
    /// <c>Dispose</c>, matching the subscribe-in-<c>OnInitialized</c>/unsubscribe-in-<c>Dispose</c>
    /// pattern design.md describes.
    /// </summary>
    IDisposable Subscribe(Action<RunEvent> handler);
}
