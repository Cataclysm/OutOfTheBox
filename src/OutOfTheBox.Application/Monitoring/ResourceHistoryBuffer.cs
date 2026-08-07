// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;

namespace OutOfTheBox.Application.Monitoring;

/// <summary>One live sample point in a <see cref="ResourceHistoryBuffer"/> series.</summary>
public sealed record ResourceHistoryPoint(DateTimeOffset Timestamp, double CpuPercent, long RamBytes);

/// <summary>
/// An in-memory 10-minute circular buffer per live series (the host, and each in-flight run) -
/// per design.md's "Live rolling window" decision: independent of the persisted
/// <c>RunResourceSamples</c> series, so the Status view's live graphs never need a DB round trip.
/// Pure in-memory state with no external dependency, so - like <see cref="Concurrency.RunRegistry"/> -
/// it lives directly in Application as a concrete singleton rather than behind an
/// Infrastructure-implemented interface. The host series uses <see cref="HostSeriesKey"/>; a run's
/// series is keyed by its run id.
/// </summary>
public sealed class ResourceHistoryBuffer(IClock clock)
{
    /// <summary>The series key for host-wide figures, as opposed to a specific run's.</summary>
    public const string HostSeriesKey = "host";

    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, List<ResourceHistoryPoint>> _series = new();

    /// <summary>Appends a point to <paramref name="seriesKey"/>'s buffer, evicting points older than the 10-minute window.</summary>
    public void Add(string seriesKey, DateTimeOffset timestamp, double cpuPercent, long ramBytes)
    {
        var points = _series.GetOrAdd(seriesKey, static _ => []);

        lock (points)
        {
            points.Add(new ResourceHistoryPoint(timestamp, cpuPercent, ramBytes));
            points.RemoveAll(p => p.Timestamp < clock.UtcNow - WindowDuration);
        }
    }

    /// <summary>The current (already-evicted) points for <paramref name="seriesKey"/>, oldest first. Empty if the series doesn't exist.</summary>
    public IReadOnlyList<ResourceHistoryPoint> Get(string seriesKey)
    {
        if (!_series.TryGetValue(seriesKey, out var points))
        {
            return [];
        }

        lock (points)
        {
            return [.. points];
        }
    }

    /// <summary>Removes a run's series entirely - called once its run reaches a terminal state, so a finished run's buffer doesn't linger for the rest of the service's lifetime.</summary>
    public void Remove(string seriesKey) => _series.TryRemove(seriesKey, out _);
}
