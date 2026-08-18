// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// In-process publish/subscribe for repository-stats cache updates, mirroring
/// <see cref="Events.IRunEventBus"/>/<see cref="Monitoring.IResourceEventBus"/>'s
/// subscribe/<c>StateHasChanged</c>/unsubscribe pattern - reuses the established live-update
/// mechanism rather than inventing a second one. Exists because <c>RepositoryStatsCache</c> is
/// updated from two places (an immediate post-clone compute, and <c>RepositoryStatsSampler</c>'s
/// slow-cadence-plus-event-driven background recompute) that a dashboard component has no other way
/// to learn about: unlike a run's own lifecycle, a cache update isn't itself a <see cref="Events.RunEvent"/>,
/// so a subscriber only interested in "did any repository's stats just change" would otherwise have
/// no signal at all between page loads.
/// </summary>
public interface IRepositoryStatsEventBus
{
    /// <summary>Publishes that <paramref name="repositoryName"/>'s cached stats changed, to every current subscriber. Never throws on a subscriber's own failure.</summary>
    void Publish(string repositoryName);

    /// <summary>Registers <paramref name="handler"/> to be invoked for every subsequently published change. Dispose the returned handle to unsubscribe.</summary>
    IDisposable Subscribe(Action<string> handler);
}
