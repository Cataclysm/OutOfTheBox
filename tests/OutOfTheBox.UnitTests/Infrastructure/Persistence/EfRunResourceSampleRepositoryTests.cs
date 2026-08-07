// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Persistence;

namespace OutOfTheBox.UnitTests.Infrastructure.Persistence;

public sealed class EfRunResourceSampleRepositoryTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();

    [Fact]
    public async Task GetSeriesAsync_returns_only_that_runs_samples_ordered_by_timestamp()
    {
        var repository = new EfRunResourceSampleRepository(_dbContextFactory.CreateContext());
        var runId = Guid.NewGuid();
        var otherRunId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;

        await repository.AddAsync(new RunResourceSample { RunId = runId, Timestamp = start.AddSeconds(6), CpuPercent = 30, RamBytes = 300 }, CancellationToken.None);
        await repository.AddAsync(new RunResourceSample { RunId = runId, Timestamp = start, CpuPercent = 10, RamBytes = 100 }, CancellationToken.None);
        await repository.AddAsync(new RunResourceSample { RunId = runId, Timestamp = start.AddSeconds(3), CpuPercent = 20, RamBytes = 200 }, CancellationToken.None);
        await repository.AddAsync(new RunResourceSample { RunId = otherRunId, Timestamp = start.AddSeconds(1), CpuPercent = 99, RamBytes = 999 }, CancellationToken.None);

        var series = await repository.GetSeriesAsync(runId, CancellationToken.None);

        Assert.Equal(3, series.Count);
        Assert.Equal([10, 20, 30], series.Select(s => s.CpuPercent));
        Assert.All(series, s => Assert.Equal(runId, s.RunId));
    }

    [Fact]
    public async Task GetSeriesAsync_returns_an_empty_series_for_a_run_with_no_samples()
    {
        var repository = new EfRunResourceSampleRepository(_dbContextFactory.CreateContext());

        var series = await repository.GetSeriesAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(series);
    }

    /// <inheritdoc />
    public void Dispose() => _dbContextFactory.Dispose();
}
