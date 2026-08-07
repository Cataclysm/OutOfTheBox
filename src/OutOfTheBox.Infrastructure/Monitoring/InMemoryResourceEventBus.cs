// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;
using OutOfTheBox.Application.Monitoring;

namespace OutOfTheBox.Infrastructure.Monitoring;

/// <inheritdoc cref="IResourceEventBus" />
/// <remarks>Same plain in-memory fan-out shape as <c>InMemoryRunEventBus</c> (Events namespace) - see that type's remarks for why this lives in Infrastructure rather than Application.</remarks>
public sealed class InMemoryResourceEventBus : IResourceEventBus
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
            catch
            {
                // A subscriber's own failure must never break the sampler's publishing loop.
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
