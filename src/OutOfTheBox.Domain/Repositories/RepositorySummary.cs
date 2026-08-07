// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// The identifying stats shown for one repository (a top-level directory under the configured
/// root) in the dashboard's Repos view, per specs/repository-management. A plain data holder -
/// computing these values (directory size, git status, active state) is Infrastructure's job.
/// </summary>
public sealed class RepositorySummary
{
    /// <summary>The repository's directory name, relative to the configured root.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether size/git status have been computed at least once yet - <see langword="false"/> for
    /// a repository the background sampler hasn't reached its first tick for since service
    /// startup, so the dashboard can show "computing…" instead of a wrong/blank size of zero.
    /// </summary>
    public required bool StatsComputed { get; init; }

    /// <summary>Total on-disk size in bytes, summed recursively. Meaningless until <see cref="StatsComputed"/> is true.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>Whether this directory is a git repository at all.</summary>
    public bool IsGitRepository { get; init; }

    /// <summary>The current branch name, if <see cref="IsGitRepository"/>.</summary>
    public string? Branch { get; init; }

    /// <summary>Whether the working tree has uncommitted changes, if <see cref="IsGitRepository"/>.</summary>
    public bool IsDirty { get; init; }

    /// <summary>Commits ahead of the configured upstream, if one exists; <see langword="null"/> if there is none.</summary>
    public int? AheadCount { get; init; }

    /// <summary>Commits behind the configured upstream, if one exists; <see langword="null"/> if there is none.</summary>
    public int? BehindCount { get; init; }

    /// <summary>Whether this repository currently holds the per-repo command lock (an in-flight <c>dotnet</c>/<c>git</c> run or clone). Sourced live, never cached.</summary>
    public required bool IsActive { get; init; }
}
