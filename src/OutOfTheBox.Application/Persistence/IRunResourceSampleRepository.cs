// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Runs;

namespace OutOfTheBox.Application.Persistence;

/// <summary>Durable storage for <see cref="RunResourceSample"/> points, per specs/run-history.</summary>
public interface IRunResourceSampleRepository
{
    /// <summary>Appends one sample to a run's resource-usage series.</summary>
    Task AddAsync(RunResourceSample sample, CancellationToken cancellationToken);

    /// <summary>Returns a run's complete resource-usage series, ordered by <see cref="RunResourceSample.Timestamp"/>.</summary>
    Task<IReadOnlyList<RunResourceSample>> GetSeriesAsync(Guid runId, CancellationToken cancellationToken);
}
