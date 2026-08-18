// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// The git-status half of <see cref="RepositoryStats"/> - everything <see cref="IRepositoryStatsProvider.ComputeGitStatusAsync"/>
/// produces, computed on its own (fast) cadence independent of the (slow, directory-walk-based) size
/// half. See <see cref="RepositoryStatsCache.SetGitStatus"/> for how the two halves merge back into
/// one cached <see cref="RepositoryStats"/> per repository.
/// </summary>
public sealed record GitStatusSnapshot(
    bool IsGitRepository,
    string? Branch,
    bool IsDirty,
    int? AheadCount,
    int? BehindCount,
    bool IsRemoteGone,
    IReadOnlyList<RepositoryRemote> Remotes,
    bool IsDetachedHead = false,
    bool NeedsCredential = false);
