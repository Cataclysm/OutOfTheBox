// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Monitoring;

/// <summary>
/// Computes one tick's full <see cref="ResourceSnapshot"/> (host CPU/RAM, service RAM, and every
/// currently-tracked run's process-tree aggregate) - per design.md, backed by
/// <c>PerformanceCounter</c>/<c>GlobalMemoryStatusEx</c>/WMI, so implementations belong in
/// Infrastructure. Stateful across calls (per-process CPU delta-sampling needs the previous tick's
/// figures), so registered as a singleton.
/// </summary>
public interface IResourceSampler
{
    /// <summary>Samples current host and per-run figures.</summary>
    Task<ResourceSnapshot> SampleAsync(CancellationToken cancellationToken);
}
