// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// The outcome of a file-browser delete/rename action (per <see cref="IRepositoryFileBrowser"/>) -
/// same synchronous, no-history-record shape as <see cref="RepositoryGitActionResult"/>, for the
/// same reason (the dashboard only needs to know whether it succeeded, not stream anything).
/// </summary>
public abstract record RepositoryFileActionResult
{
    /// <summary>The action completed successfully.</summary>
    public sealed record Succeeded : RepositoryFileActionResult;

    /// <summary>The action was attempted but failed (e.g. a locked file).</summary>
    public sealed record Failed(string? ErrorMessage) : RepositoryFileActionResult;

    /// <summary>The request was rejected before touching the filesystem.</summary>
    public sealed record Rejected(RepositoryActionRejectionReason Reason, Guid? ConflictingRunId = null) : RepositoryFileActionResult;
}
