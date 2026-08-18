// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Computes a single repository's size and git status - IO-heavy (a recursive directory walk plus
/// a handful of internal, unstreamed <c>git.exe</c> invocations), so implementations belong in
/// Infrastructure. Never modeled as a <see cref="Domain.Runs.Run"/> - this is telemetry sampling,
/// not an operator-triggered command.
/// </summary>
public interface IRepositoryStatsProvider
{
    /// <summary>Computes current size/git-status for the repository at <paramref name="repositoryPath"/> - both halves, for callers that need a full snapshot in one shot (initial sampler startup sweep, post-clone, post-run-event recompute).</summary>
    Task<RepositoryStats> ComputeAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>Computes only the git-status half (branch, dirty, ahead/behind, remote-gone, remotes) - cheap (a handful of short-lived <c>git</c> invocations, no directory walk), for the sampler's fast cadence.</summary>
    Task<GitStatusSnapshot> ComputeGitStatusAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>Computes only the total on-disk size - a full recursive directory walk, for the sampler's slow cadence.</summary>
    Task<long> ComputeSizeAsync(string repositoryPath, CancellationToken cancellationToken);
}
