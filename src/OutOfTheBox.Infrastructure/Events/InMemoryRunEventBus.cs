// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;
using OutOfTheBox.Application.Events;

namespace OutOfTheBox.Infrastructure.Events;

/// <inheritdoc cref="IRunEventBus" />
/// <remarks>
/// A plain in-memory fan-out over a concurrent subscriber set - no external dependency, but
/// implemented here rather than directly in Application (unlike <see cref="OutOfTheBox.Application.Concurrency.RunRegistry"/>,
/// which has no interface) because design.md's Architecture section explicitly lists
/// <c>IRunEventBus</c> among the ports Infrastructure implements, so dashboard components in
/// Presentation depend only on the interface, never on this concrete type.
/// </remarks>
public sealed class InMemoryRunEventBus : IRunEventBus
{
    private readonly ConcurrentDictionary<Guid, Action<RunEvent>> _subscribers = new();

    /// <inheritdoc />
    public void Publish(RunEvent runEvent)
    {
        foreach (var handler in _subscribers.Values)
        {
            try
            {
                handler(runEvent);
            }
            catch
            {
                // A subscriber's own failure (e.g. a Blazor component mid-teardown) must never
                // break the publishing code path (command execution, persistence) it's attached to.
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<RunEvent> handler)
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
