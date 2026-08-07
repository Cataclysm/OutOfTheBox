// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace OutOfTheBox.Infrastructure.Persistence;

/// <inheritdoc cref="IRunResourceSampleRepository" />
public sealed class EfRunResourceSampleRepository(OutOfTheBoxDbContext dbContext) : IRunResourceSampleRepository
{
    /// <inheritdoc />
    public async Task AddAsync(RunResourceSample sample, CancellationToken cancellationToken)
    {
        dbContext.RunResourceSamples.Add(sample);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RunResourceSample>> GetSeriesAsync(Guid runId, CancellationToken cancellationToken)
    {
        // Timestamp is a DateTimeOffset - like Run.StartedAt (see EfRunRepository.ListAsync), the
        // SQLite provider can't translate an ORDER BY over it, so the sort happens client-side
        // after filtering by RunId server-side.
        var samples = await dbContext.RunResourceSamples
            .AsNoTracking()
            .Where(s => s.RunId == runId)
            .ToListAsync(cancellationToken);

        return [.. samples.OrderBy(s => s.Timestamp)];
    }
}
