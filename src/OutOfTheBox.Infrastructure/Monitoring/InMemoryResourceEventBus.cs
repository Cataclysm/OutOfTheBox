// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;
using OutOfTheBox.Application.Monitoring;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <inheritdoc cref="IResourceEventBus" />
/// <remarks>Same plain in-memory fan-out shape as <c>InMemoryRunEventBus</c> (Events namespace) - see that type's remarks for why this lives in Infrastructure rather than Application.</remarks>
public sealed class InMemoryResourceEventBus(ILogger<InMemoryResourceEventBus> logger) : IResourceEventBus
{
    private readonly ConcurrentDictionary<Guid, Action<ResourceSnapshot>> _subscribers = new();

    /// <inheritdoc />
    public void Publish(ResourceSnapshot snapshot)
    {
        foreach (var handler in _subscribers.Values)
        {
            try
            {
                handler(snapshot);
            }
            catch (Exception ex)
            {
                // A subscriber's own failure must never break the sampler's publishing loop -
                // logged at Warning (see InMemoryRunEventBus's remark on why not Error). This ticks
                // every few seconds, so the log line intentionally carries no per-snapshot detail
                // beyond the exception itself, to avoid becoming a repeat-offender noise source.
                logger.LogWarning(ex, "A resource-event subscriber threw handling a snapshot publish.");
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<ResourceSnapshot> handler)
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
