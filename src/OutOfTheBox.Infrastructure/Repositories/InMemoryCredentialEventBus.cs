// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Collections.Concurrent;
using OutOfTheBox.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="ICredentialEventBus" />
/// <remarks>Same plain in-memory fan-out shape as <c>InMemoryRepositoryStatsEventBus</c>/<c>InMemoryRunEventBus</c> - see those types' remarks for why this lives in Infrastructure rather than Application.</remarks>
public sealed class InMemoryCredentialEventBus(ILogger<InMemoryCredentialEventBus> logger) : ICredentialEventBus
{
    private readonly ConcurrentDictionary<Guid, Action> _subscribers = new();

    /// <inheritdoc />
    public void Publish()
    {
        foreach (var handler in _subscribers.Values)
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                // A subscriber's own failure (e.g. a Blazor component mid-teardown) must never break
                // the publishing code path (an MCP tool call, the Credentials page's own dialogs) it's
                // attached to - logged at Warning (see InMemoryRunEventBus's remark on why not Error).
                logger.LogWarning(ex, "A credential-event subscriber threw handling a publish.");
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action handler)
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
