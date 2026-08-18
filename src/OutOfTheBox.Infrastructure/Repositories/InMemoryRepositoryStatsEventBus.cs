// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Collections.Concurrent;
using OutOfTheBox.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IRepositoryStatsEventBus" />
/// <remarks>Same plain in-memory fan-out shape as <c>InMemoryRunEventBus</c>/<c>InMemoryResourceEventBus</c> - see those types' remarks for why this lives in Infrastructure rather than Application.</remarks>
public sealed class InMemoryRepositoryStatsEventBus(ILogger<InMemoryRepositoryStatsEventBus> logger) : IRepositoryStatsEventBus
{
    private readonly ConcurrentDictionary<Guid, Action<string>> _subscribers = new();

    /// <inheritdoc />
    public void Publish(string repositoryName)
    {
        foreach (var handler in _subscribers.Values)
        {
            try
            {
                handler(repositoryName);
            }
            catch (Exception ex)
            {
                // A subscriber's own failure (e.g. a Blazor component mid-teardown) must never
                // break the publishing code path (clone completion, the stats sampler's tick) it's
                // attached to - logged at Warning (see InMemoryRunEventBus's remark on why not Error).
                logger.LogWarning(ex, "A repository-stats subscriber threw handling a publish for '{RepositoryName}'.", repositoryName);
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<string> handler)
    {
        var id = Guid.NewGuid();
        _subscribers[id] = handler;
        return new Subscription(() => _subscribers.TryRemove(id, out _));
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
