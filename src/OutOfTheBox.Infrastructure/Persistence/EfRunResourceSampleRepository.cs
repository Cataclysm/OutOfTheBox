// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

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
