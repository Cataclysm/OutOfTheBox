// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Runs;

/// <summary>
/// One point in a <see cref="Run"/>'s resource-usage time series: the aggregate CPU/RAM of its
/// process tree at one sampler tick (per specs/host-resource-monitoring), or - for a
/// <see cref="RunKind.FileTransfer"/>, which spawns no process tree of its own - the
/// host-level CPU/RAM at that tick instead (see design.md's "resource sampling for transfers"
/// decision). Not produced for <see cref="RunKind.RepositoryDelete"/>, which has no meaningful
/// in-flight duration to sample. Network/disk figures are always host-level, for every run kind
/// (not just transfers) - Windows has no per-process network-only counter, and disk I/O is sampled
/// machine-wide ("PhysicalDisk"/"_Total"), so there is no isolated per-run figure possible for
/// either. They're nullable (unlike <see cref="CpuPercent"/>/<see cref="RamBytes"/>, always present
/// since day one) because a row persisted before this pair of columns existed genuinely has no
/// recorded value for them - not the same thing as a real zero reading.
/// </summary>
public sealed class RunResourceSample
{
    /// <summary>The run this sample belongs to.</summary>
    public required Guid RunId { get; init; }

    /// <summary>When this sample was taken.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Aggregate CPU utilization at this tick, as a percentage.</summary>
    public required double CpuPercent { get; init; }

    /// <summary>Aggregate resident memory at this tick, in bytes.</summary>
    public required long RamBytes { get; init; }

    /// <summary>Host-wide network bytes sent per second at this tick, tagged onto this run - see the type's own remarks.</summary>
    public double? NetworkBytesSentPerSecond { get; init; }

    /// <summary>Host-wide network bytes received per second at this tick, tagged onto this run - see the type's own remarks.</summary>
    public double? NetworkBytesReceivedPerSecond { get; init; }

    /// <summary>Host-wide disk read bytes per second (all drives combined) at this tick, tagged onto this run - see the type's own remarks.</summary>
    public double? DiskReadBytesPerSecond { get; init; }

    /// <summary>Host-wide disk write bytes per second (all drives combined) at this tick, tagged onto this run - see the type's own remarks.</summary>
    public double? DiskWriteBytesPerSecond { get; init; }
}
