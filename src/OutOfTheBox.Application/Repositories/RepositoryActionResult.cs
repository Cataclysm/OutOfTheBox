// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>Why a <see cref="IRepositoryManager"/> clone/delete request was rejected without starting.</summary>
public enum RepositoryActionRejectionReason
{
    /// <summary>The supplied name resolves outside the configured root.</summary>
    InvalidName,

    /// <summary>(Clone only) the target name already names an existing repository.</summary>
    AlreadyExists,

    /// <summary>(Delete only) the named repository does not exist.</summary>
    NotFound,

    /// <summary>The repository's per-repo lock is already held by another in-flight run.</summary>
    Busy,
}

/// <summary>
/// The outcome of a <see cref="IRepositoryManager"/> clone/delete call - either accepted (and
/// assigned a run id the caller can track/cancel) or rejected before anything started, with enough
/// detail for the dashboard to explain why.
/// </summary>
public abstract record RepositoryActionResult
{
    /// <summary>The request was accepted and is now running under <paramref name="RunId"/>.</summary>
    public sealed record Accepted(Guid RunId) : RepositoryActionResult;

    /// <summary>The request was rejected before acquiring any lock or touching the filesystem.</summary>
    public sealed record Rejected(RepositoryActionRejectionReason Reason, Guid? ConflictingRunId = null) : RepositoryActionResult;
}
