// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Monitoring;

/// <summary>One process's figures at a single sampler tick.</summary>
public sealed record ProcessResourceSample(int ProcessId, string ProcessName, DateTime StartTime, double CpuPercent, long RamBytes);

/// <summary>
/// One <c>dotnet</c>/<c>git</c>/clone run's aggregate figures at a single tick - the sum of its
/// process tree's per-process figures, per design.md's "Per-run aggregation" decision.
/// </summary>
public sealed record RunResourceAggregate(Guid RunId, double CpuPercent, long RamBytes, IReadOnlyList<ProcessResourceSample> Processes);

/// <summary>
/// Host-wide CPU/RAM/network/disk figures at a single tick. Network and disk figures are
/// already-per-second rates (network summed across every network interface; disk read straight from
/// the "PhysicalDisk"/"_Total" counter, which is itself already an all-drives sum on Windows), not
/// cumulative counters. <see cref="DiskReadBytesPerSecond"/>/<see cref="DiskWriteBytesPerSecond"/>
/// default to 0 so every pre-existing positional-argument call site (tests, mainly) keeps compiling
/// unchanged.
/// </summary>
public sealed record HostResourceSample(
    double TotalCpuPercent,
    IReadOnlyList<double> PerCoreCpuPercent,
    long TotalRamBytes,
    long AvailableRamBytes,
    long ServiceRamBytes,
    double NetworkBytesSentPerSecond,
    double NetworkBytesReceivedPerSecond,
    double DiskReadBytesPerSecond = 0,
    double DiskWriteBytesPerSecond = 0);

/// <summary>Everything sampled in one tick: host figures plus every currently-tracked run's aggregate.</summary>
public sealed record ResourceSnapshot(DateTimeOffset Timestamp, HostResourceSample Host, IReadOnlyList<RunResourceAggregate> Runs);
