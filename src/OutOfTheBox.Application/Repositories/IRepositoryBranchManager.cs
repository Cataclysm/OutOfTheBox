// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Lists and changes what a repository has checked out - its branches, and (via
/// <see cref="SwitchBranchAsync"/>/<see cref="CheckoutCommitAsync"/>) which one, or which specific
/// commit, is currently checked out. Split out of the former single <c>IRepositoryManager</c> - see
/// its own remarks - since the repository detail page's branch dropdown and the commit graph's own
/// checkout actions only ever need this slice.
/// </summary>
public interface IRepositoryBranchManager
{
    /// <summary>
    /// Enumerates the named repository's local and remote-tracking branches (<c>git branch -a</c>),
    /// for the detail subpage's branch-switch dropdown.
    /// </summary>
    Task<IReadOnlyList<RepositoryBranch>> ListBranchesAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Switches the named repository's checked-out branch to <paramref name="branch"/>. If it names
    /// an existing local branch, checks it out directly; if it names a remote branch with no local
    /// counterpart, first creates a local branch tracking it, then checks that out - per
    /// specs/repository-management's "Repository detail provides a branch-switch control"
    /// requirement.
    /// </summary>
    Task<RepositoryGitActionResult> SwitchBranchAsync(string name, string branch, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates the branches of a remote repository at <paramref name="url"/> without cloning it
    /// (<c>git ls-remote --heads</c>), for the dashboard's clone dialog to populate its branch
    /// dropdown once the operator has entered a source URL. Returns an empty list if the lookup
    /// fails (unreachable/invalid URL) - failure here never blocks cloning with no explicit branch.
    /// </summary>
    Task<IReadOnlyList<string>> ListRemoteBranchesAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Checks out the specific commit <paramref name="hash"/> in the named repository, resulting in
    /// a detached HEAD there - per specs/repository-management's "A commit can be checked out as a
    /// detached HEAD" requirement. Lock-guarded like every other mutating repository action; the
    /// dashboard requires confirmation before calling this given it changes the checked-out state.
    /// </summary>
    Task<RepositoryGitActionResult> CheckoutCommitAsync(string name, string hash, CancellationToken cancellationToken);
}
