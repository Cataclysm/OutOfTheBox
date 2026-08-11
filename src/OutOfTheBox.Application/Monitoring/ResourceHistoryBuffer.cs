// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;

namespace OutOfTheBox.Application.Monitoring;

/// <summary>
/// One live sample point in a <see cref="ResourceHistoryBuffer"/> series.
/// <see cref="PerCoreCpuPercent"/> is only ever populated for the host series
/// (<see cref="ResourceHistoryBuffer.HostSeriesKey"/>) - a run/transfer has no per-core breakdown of
/// its own to report. Network/disk figures, by contrast, are host-measured (Windows has no
/// per-process network-only counter, and "PhysicalDisk" is a whole-machine category) but are still
/// tagged onto every run/transfer's own point at the same tick, the same "no isolated figure is
/// possible, use the host's" precedent already established for a transfer's CPU/RAM.
/// </summary>
public sealed record ResourceHistoryPoint(
    DateTimeOffset Timestamp,
    double CpuPercent,
    long RamBytes,
    IReadOnlyList<double>? PerCoreCpuPercent = null,
    double? NetworkBytesSentPerSecond = null,
    double? NetworkBytesReceivedPerSecond = null,
    double? DiskReadBytesPerSecond = null,
    double? DiskWriteBytesPerSecond = null);

/// <summary>
/// An in-memory 20-minute circular buffer per live series (the host, and each in-flight run) -
/// per design.md's "Live rolling window" decision: independent of the persisted
/// <c>RunResourceSamples</c> series, so the Status view's live graphs never need a DB round trip.
/// Pure in-memory state with no external dependency, so - like <see cref="Concurrency.RunRegistry"/> -
/// it lives directly in Application as a concrete singleton rather than behind an
/// Infrastructure-implemented interface. The host series uses <see cref="HostSeriesKey"/>; a run's
/// series is keyed by its run id. 20 minutes, not 10, per direct instruction that the Status page's
/// host CPU graph specifically should show 20 minutes - every other live graph still only ever
/// displays the most recent 10 of those 20 via <see cref="Get(string, TimeSpan?)"/>'s own window
/// parameter, so this buffer just needs to actually retain the longer of the two.
/// </summary>
public sealed class ResourceHistoryBuffer(IClock clock)
{
    /// <summary>The series key for host-wide figures, as opposed to a specific run's.</summary>
    public const string HostSeriesKey = "host";

    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(20);

    private readonly ConcurrentDictionary<string, List<ResourceHistoryPoint>> _series = new();

    /// <summary>Appends a point to <paramref name="seriesKey"/>'s buffer, evicting points older than the 20-minute window.</summary>
    public void Add(
        string seriesKey,
        DateTimeOffset timestamp,
        double cpuPercent,
        long ramBytes,
        IReadOnlyList<double>? perCoreCpuPercent = null,
        double? networkBytesSentPerSecond = null,
        double? networkBytesReceivedPerSecond = null,
        double? diskReadBytesPerSecond = null,
        double? diskWriteBytesPerSecond = null)
    {
        var points = _series.GetOrAdd(seriesKey, static _ => []);

        lock (points)
        {
            points.Add(new ResourceHistoryPoint(
                timestamp, cpuPercent, ramBytes, perCoreCpuPercent,
                networkBytesSentPerSecond, networkBytesReceivedPerSecond,
                diskReadBytesPerSecond, diskWriteBytesPerSecond));
            points.RemoveAll(p => p.Timestamp < clock.UtcNow - WindowDuration);
        }
    }

    /// <summary>
    /// The current (already-evicted) points for <paramref name="seriesKey"/>, oldest first, empty if
    /// the series doesn't exist. <paramref name="window"/> further restricts the result to points no
    /// older than that duration before now (e.g. the Status page's own graphs other than the host
    /// CPU one, which alone shows the buffer's full 20-minute retention) - omit it to get everything
    /// the buffer currently retains.
    /// </summary>
    public IReadOnlyList<ResourceHistoryPoint> Get(string seriesKey, TimeSpan? window = null)
    {
        if (!_series.TryGetValue(seriesKey, out var points))
        {
            return [];
        }

        lock (points)
        {
            if (window is not { } w)
            {
                return [.. points];
            }

            var cutoff = clock.UtcNow - w;
            return [.. points.Where(p => p.Timestamp >= cutoff)];
        }
    }

    /// <summary>Removes a run's series entirely - called once its run reaches a terminal state, so a finished run's buffer doesn't linger for the rest of the service's lifetime.</summary>
    public void Remove(string seriesKey) => _series.TryRemove(seriesKey, out _);
}
