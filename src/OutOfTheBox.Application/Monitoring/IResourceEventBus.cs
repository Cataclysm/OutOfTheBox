// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Monitoring;

/// <summary>
/// In-process publish/subscribe for resource-sampler ticks, mirroring
/// <see cref="Events.IRunEventBus"/>'s subscribe/<c>StateHasChanged</c>/unsubscribe pattern (per
/// design.md's "Resource sampling cadence" decision) - reuses the established live-update
/// mechanism rather than inventing a second one.
/// </summary>
public interface IResourceEventBus
{
    /// <summary>Publishes a snapshot to every current subscriber. Never throws on a subscriber's own failure.</summary>
    void Publish(ResourceSnapshot snapshot);

    /// <summary>Registers <paramref name="handler"/> to be invoked for every subsequently published snapshot. Dispose the returned handle to unsubscribe.</summary>
    IDisposable Subscribe(Action<ResourceSnapshot> handler);
}
