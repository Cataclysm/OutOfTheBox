// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace OutOfTheBox.Infrastructure.Persistence;

/// <inheritdoc cref="IRunRepository" />
public sealed class EfRunRepository(OutOfTheBoxDbContext dbContext) : IRunRepository
{
    /// <inheritdoc />
    public async Task AddAsync(Run run, CancellationToken cancellationToken)
    {
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Run run, CancellationToken cancellationToken)
    {
        dbContext.Runs.Update(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Run?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Run>> ListAsync(RunQuery query, CancellationToken cancellationToken)
    {
        var runs = dbContext.Runs.AsNoTracking().AsQueryable();

        if (query.Kinds is { Count: > 0 } kinds)
        {
            runs = runs.Where(r => kinds.Contains(r.Kind));
        }

        if (query.Outcomes is { Count: > 0 } outcomes)
        {
            runs = runs.Where(r => outcomes.Contains(r.Outcome));
        }

        if (!string.IsNullOrEmpty(query.RepositoryPath))
        {
            runs = runs.Where(r => r.RepositoryPath == query.RepositoryPath);
        }

        // Kind/Outcome/RepositoryPath filter server-side (plain, unconverted columns); SearchText and the
        // final ordering happen client-side below, after materializing this already-narrowed set.
        var matches = await runs.ToListAsync(cancellationToken);

        if (!string.IsNullOrEmpty(query.SearchText))
        {
            // Per specs/run-history's "History supports free-text search" requirement: repository,
            // arguments, file path, and clone source URL - not stdout/stderr, which isn't part
            // of that requirement. Matched client-side rather than via EF.Functions.Like: Arguments
            // is mapped through a value converter (JSON-encoded list), and asking EF.Property<string>
            // for its converted (provider-side) representation to build a SQL LIKE clause causes
            // EF Core to misapply that same converter to the *pattern* parameter at execution time
            // (an EF Core value-converter/EF.Property interaction bug, not a modeling mistake) -
            // this is safe at the row counts this table is expected to reach (see the "no
            // retention policy" trade-off already accepted for this table's growth).
            matches = [.. matches.Where(r => Matches(r, query.SearchText))];
        }

        // StartedAt is a DateTimeOffset - the SQLite provider can't translate an ORDER BY over it,
        // so sorting happens client-side too (see above).
        return [.. matches.OrderByDescending(r => r.StartedAt)];
    }

    /// <inheritdoc />
    public Task<int> UpdateRepositoryPathAsync(string oldRepositoryPath, string newRepositoryPath, CancellationToken cancellationToken) =>
        dbContext.Runs
            .Where(r => r.RepositoryPath == oldRepositoryPath)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.RepositoryPath, newRepositoryPath), cancellationToken);

    private static bool Matches(Run run, string searchText) =>
        run.RepositoryPath.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
        (run.Arguments is not null && run.Arguments.Any(a => a.Contains(searchText, StringComparison.OrdinalIgnoreCase))) ||
        (run.FilePath is not null && run.FilePath.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
        (run.SourceUrl is not null && run.SourceUrl.Contains(searchText, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public async Task<int> ReconcileInterruptedAsync(CancellationToken cancellationToken)
    {
        var stillRunning = await dbContext.Runs
            .Where(r => r.Outcome == RunOutcome.Running)
            .ToListAsync(cancellationToken);

        foreach (var run in stillRunning)
        {
            run.Outcome = RunOutcome.Interrupted;
            run.CompletedAt = DateTimeOffset.UtcNow;
        }

        if (stillRunning.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return stillRunning.Count;
    }
}
