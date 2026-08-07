// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Computes a single repository's size and git status - IO-heavy (a recursive directory walk plus
/// a handful of internal, unstreamed <c>git.exe</c> invocations), so implementations belong in
/// Infrastructure. Never modeled as a <see cref="Domain.Runs.Run"/> - this is telemetry sampling,
/// not an operator-triggered command.
/// </summary>
public interface IRepositoryStatsProvider
{
    /// <summary>Computes current size/git-status for the repository at <paramref name="repoPath"/>.</summary>
    Task<RepositoryStats> ComputeAsync(string repoPath, CancellationToken cancellationToken);
}
