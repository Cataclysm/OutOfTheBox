// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// The outcome of a pull/push/force-push/fetch/clean (<see cref="IRepositoryGitActions"/>), rename
/// (<see cref="IRepositoryManager"/>), or branch-switch/commit-checkout
/// (<see cref="IRepositoryBranchManager"/>) action - unlike <see cref="RepositoryActionResult"/>'s
/// clone/delete outcomes, these run to completion synchronously (no run id, no history record, no
/// streamed output) since the dashboard only needs to recolor an icon (or close a dialog) on
/// completion, per specs/repository-management's "Dashboard-only pull/push/force-push/fetch/clean
/// actions" requirement - every one of these is the same shape of action (a quick, local,
/// lock-guarded mutation) even though not all of them are literally a git command (rename isn't).
/// </summary>
public abstract record RepositoryGitActionResult
{
    /// <summary>The git invocation completed with exit code 0.</summary>
    public sealed record Succeeded : RepositoryGitActionResult;

    /// <summary>The git invocation ran but exited non-zero, or could not start at all.</summary>
    public sealed record Failed(string? ErrorMessage) : RepositoryGitActionResult;

    /// <summary>The request was rejected before acquiring any lock or invoking git.</summary>
    public sealed record Rejected(RepositoryActionRejectionReason Reason, Guid? ConflictingRunId = null) : RepositoryGitActionResult;
}
