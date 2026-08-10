// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Holds the most recently computed <see cref="RepositoryStats"/> per repository name, written by
/// the background stats sampler (per design.md's "Repository stats" decision: a slow cadence plus
/// event-driven recompute, not the fast resource-sampler loop). Pure in-memory state with no
/// external dependency, so - like <see cref="Concurrency.RunRegistry"/> - it lives directly in
/// Application as a concrete singleton rather than behind an Infrastructure-implemented interface.
/// </summary>
public sealed class RepositoryStatsCache
{
    private readonly ConcurrentDictionary<string, RepositoryStats> _stats = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cached stats for <paramref name="name"/>, or <see langword="null"/> if none have been computed yet.</summary>
    public RepositoryStats? TryGet(string name) => _stats.GetValueOrDefault(name);

    /// <summary>Records freshly computed stats for <paramref name="name"/>, replacing any previous value.</summary>
    public void Set(string name, RepositoryStats stats) => _stats[name] = stats;

    /// <summary>
    /// Merges a freshly computed git-status half into <paramref name="name"/>'s cached stats,
    /// preserving whatever size half (if any) is already cached - the fast git-status cadence and the
    /// slow size cadence each only ever have one half of the picture at a time. If nothing is cached
    /// yet, the size half starts at 0 (displayed as "Computing…" via <c>RepositorySummary.StatsComputed</c>
    /// only checking for cache presence, not size specifically - the next size tick fills it in).
    /// </summary>
    public void SetGitStatus(string name, GitStatusSnapshot gitStatus) =>
        _stats.AddOrUpdate(
            name,
            static (_, snapshot) => new RepositoryStats(0, snapshot.IsGitRepository, snapshot.Branch, snapshot.IsDirty, snapshot.AheadCount, snapshot.BehindCount, snapshot.IsRemoteGone, snapshot.Remotes, snapshot.IsDetachedHead, snapshot.NeedsCredential),
            static (_, existing, snapshot) => existing with
            {
                IsGitRepository = snapshot.IsGitRepository,
                Branch = snapshot.Branch,
                IsDirty = snapshot.IsDirty,
                AheadCount = snapshot.AheadCount,
                BehindCount = snapshot.BehindCount,
                IsRemoteGone = snapshot.IsRemoteGone,
                Remotes = snapshot.Remotes,
                IsDetachedHead = snapshot.IsDetachedHead,
                NeedsCredential = snapshot.NeedsCredential,
            },
            gitStatus);

    /// <summary>Merges a freshly computed total size into <paramref name="name"/>'s cached stats, preserving whatever git-status half is already cached. See <see cref="SetGitStatus"/>'s own remarks.</summary>
    public void SetSize(string name, long totalSizeBytes) =>
        _stats.AddOrUpdate(
            name,
            static (_, size) => new RepositoryStats(size, IsGitRepository: false, Branch: null, IsDirty: false, AheadCount: null, BehindCount: null),
            static (_, existing, size) => existing with { TotalSizeBytes = size },
            totalSizeBytes);

    /// <summary>Removes any cached stats for <paramref name="name"/> - called once a repository is deleted, so a stale entry can't outlive its directory.</summary>
    public void Remove(string name) => _stats.TryRemove(name, out _);
}
