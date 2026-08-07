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

    /// <summary>Removes any cached stats for <paramref name="name"/> - called once a repository is deleted, so a stale entry can't outlive its directory.</summary>
    public void Remove(string name) => _stats.TryRemove(name, out _);
}
