// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Runs;

namespace OutOfTheBox.Application.Persistence;

/// <summary>Durable storage for <see cref="Run"/> records, per specs/run-history.</summary>
public interface IRunRepository
{
    /// <summary>Inserts a new run record, typically with <see cref="Run.Outcome"/> still <see cref="RunOutcome.Running"/>.</summary>
    Task AddAsync(Run run, CancellationToken cancellationToken);

    /// <summary>Persists changes made to a previously-added <see cref="Run"/> (e.g. its terminal fields).</summary>
    Task UpdateAsync(Run run, CancellationToken cancellationToken);

    /// <summary>Retrieves one run's full record by id, or <see langword="null"/> if unknown.</summary>
    Task<Run?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Lists runs most-recent-first, restricted to those matching every criterion set on <paramref name="query"/>.</summary>
    Task<IReadOnlyList<Run>> ListAsync(RunQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Repoints every run recorded against <paramref name="oldRepositoryPath"/> to
    /// <paramref name="newRepositoryPath"/> instead, so a renamed repository's history (including the
    /// clone run its own clone source URL is read from) stays attributed to it under its new name.
    /// Returns the number of records updated.
    /// </summary>
    Task<int> UpdateRepositoryPathAsync(string oldRepositoryPath, string newRepositoryPath, CancellationToken cancellationToken);

    /// <summary>
    /// Reconciles every run still recorded as <see cref="RunOutcome.Running"/> (left behind by a
    /// prior process that didn't shut down cleanly) to <see cref="RunOutcome.Interrupted"/>.
    /// Returns the number of records reconciled.
    /// </summary>
    Task<int> ReconcileInterruptedAsync(CancellationToken cancellationToken);
}
