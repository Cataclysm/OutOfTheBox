// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Lists, clones, deletes, and renames repositories on the operator's behalf - called directly from
/// Blazor component code-behind, never through an HTTP endpoint (per specs/repository-management's
/// "reachable only from the authenticated dashboard" requirement; see design.md's "Repository
/// management" decision, the same in-process pattern as the resource-monitoring kill action). The
/// concrete <c>RepositoryManager</c> also implements <see cref="IRepositoryGitActions"/>,
/// <see cref="IRepositoryBranchManager"/>, and <see cref="IRepositoryHistoryReader"/> - split into
/// four narrower interfaces (repository lifecycle, git sync actions, branch management, and read-only
/// commit/file history) so each consumer depends only on the slice it actually calls, rather than one
/// fifteen-method interface every caller pulled in regardless of which two or three methods it used.
/// </summary>
public interface IRepositoryManager
{
    /// <summary>Lists every repository under the configured root with its current stats and active state.</summary>
    Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts cloning <paramref name="url"/> into a new repository named <paramref name="name"/>,
    /// optionally checking out <paramref name="branch"/> instead of the remote's default. Returns as
    /// soon as the clone is accepted and started - the clone itself keeps running in the background,
    /// visible in Status/History via the same run id, per specs/service-dashboard's "the clone
    /// starts, appears as an in-flight run" requirement.
    /// </summary>
    Task<RepositoryActionResult> CloneAsync(string url, string name, string? branch, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the repository named <paramref name="name"/>, recursively and permanently. Unlike
    /// <see cref="CloneAsync"/>, this runs to completion before returning - a directory delete has
    /// no incremental progress worth streaming (per design.md's "Repository delete" decision).
    /// </summary>
    Task<RepositoryActionResult> DeleteAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Renames the repository named <paramref name="name"/> to <paramref name="newName"/> - a plain
    /// directory move confined to the configured root, same as every other repository-name
    /// resolution. This only changes what this service calls the repository; its git remotes,
    /// history, and working tree contents are untouched. Runs to completion before returning.
    /// </summary>
    Task<RepositoryGitActionResult> RenameAsync(string name, string newName, CancellationToken cancellationToken);

    /// <summary>
    /// The URL the named repository was cloned from through this service, sourced from its most
    /// recent completed <c>RepositoryClone</c> history record (per <c>run-history</c>) - not
    /// persisted separately. <see langword="null"/> if unknown (a pre-existing directory this
    /// service never cloned, or the history record has since been pruned).
    /// </summary>
    Task<string?> GetCloneSourceUrlAsync(string name, CancellationToken cancellationToken);
}
