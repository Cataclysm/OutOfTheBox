// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Lists, clones, and deletes repositories on the operator's behalf - called directly from Blazor
/// component code-behind, never through an HTTP endpoint (per specs/repository-management's
/// "reachable only from the authenticated dashboard" requirement; see design.md's "Repository
/// management" decision, the same in-process pattern as the resource-monitoring kill action).
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

    /// <summary>Runs <c>git pull</c> against the named repository, holding its per-repository lock for the duration. Runs to completion before returning - no streamed output, per specs/repository-management's "Dashboard-only pull/push/force-push/fetch/clean actions" requirement.</summary>
    Task<RepositoryGitActionResult> PullAsync(string name, CancellationToken cancellationToken);

    /// <summary>Runs <c>git push</c> against the named repository. See <see cref="PullAsync"/>'s own remarks.</summary>
    Task<RepositoryGitActionResult> PushAsync(string name, CancellationToken cancellationToken);

    /// <summary>Runs <c>git push --force</c> against the named repository. See <see cref="PullAsync"/>'s own remarks; the dashboard requires confirmation before calling this given its irreversibility.</summary>
    Task<RepositoryGitActionResult> ForcePushAsync(string name, CancellationToken cancellationToken);

    /// <summary>Runs <c>git fetch</c> against the named repository. See <see cref="PullAsync"/>'s own remarks.</summary>
    Task<RepositoryGitActionResult> FetchAsync(string name, CancellationToken cancellationToken);

    /// <summary>Runs <c>git clean -xdf</c> against the named repository. See <see cref="PullAsync"/>'s own remarks; the dashboard requires confirmation before calling this given its irreversibility.</summary>
    Task<RepositoryGitActionResult> CleanAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// The URL the named repository was cloned from through this service, sourced from its most
    /// recent completed <c>RepositoryClone</c> history record (per <c>run-history</c>) - not
    /// persisted separately. <see langword="null"/> if unknown (a pre-existing directory this
    /// service never cloned, or the history record has since been pruned).
    /// </summary>
    Task<string?> GetCloneSourceUrlAsync(string name, CancellationToken cancellationToken);

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
    /// Lists up to <paramref name="take"/> commits (skipping the first <paramref name="skip"/>)
    /// reachable from any branch or tag (<c>git log --all</c>), most-recent-first, for the detail
    /// subpage's commit graph. Returns an empty list for an invalid name or a repository that isn't
    /// a git repository - never throws for those cases.
    /// </summary>
    Task<IReadOnlyList<CommitSummary>> ListCommitsAsync(string name, int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Checks out the specific commit <paramref name="hash"/> in the named repository, resulting in
    /// a detached HEAD there - per specs/repository-management's "A commit can be checked out as a
    /// detached HEAD" requirement. Lock-guarded like every other mutating repository action; the
    /// dashboard requires confirmation before calling this given it changes the checked-out state.
    /// </summary>
    Task<RepositoryGitActionResult> CheckoutCommitAsync(string name, string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the full detail of one commit (<c>git show</c>) - full message body, separate author/
    /// committer identity, and the list of files it touched - for the commit detail subpage.
    /// <see langword="null"/> for an invalid repository name, a repository that isn't a git
    /// repository, or a hash it doesn't contain.
    /// </summary>
    Task<CommitDetail?> GetCommitDetailAsync(string name, string hash, CancellationToken cancellationToken);
}
