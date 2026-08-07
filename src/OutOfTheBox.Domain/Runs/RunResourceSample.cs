// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Runs;

/// <summary>
/// One point in a <see cref="Run"/>'s resource-usage time series: the aggregate CPU/RAM of its
/// process tree at one sampler tick (per specs/host-resource-monitoring), or - for a
/// <see cref="RunKind.ArtifactTransfer"/>, which spawns no process tree of its own - the
/// host-level CPU/RAM at that tick instead (see design.md's "resource sampling for transfers"
/// decision). Not produced for <see cref="RunKind.RepositoryDelete"/>, which has no meaningful
/// in-flight duration to sample.
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
}
