// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// The size/git-status figures <see cref="IRepositoryStatsProvider"/> computes for one repository -
/// everything <see cref="Domain.Repositories.RepositorySummary"/> needs except <c>Name</c> (the
/// caller already knows that) and <c>IsActive</c> (sourced live from <see cref="Concurrency.RunRegistry"/>, never cached).
/// </summary>
public sealed record RepositoryStats(
    long TotalSizeBytes,
    bool IsGitRepository,
    string? Branch,
    bool IsDirty,
    int? AheadCount,
    int? BehindCount,
    bool IsRemoteGone = false,
    IReadOnlyList<RepositoryRemote>? Remotes = null,
    bool IsDetachedHead = false,
    bool NeedsCredential = false)
{
    /// <summary>Never null on a constructed instance - defaults to empty so callers don't need a null check.</summary>
    public IReadOnlyList<RepositoryRemote> Remotes { get; init; } = Remotes ?? [];
}
