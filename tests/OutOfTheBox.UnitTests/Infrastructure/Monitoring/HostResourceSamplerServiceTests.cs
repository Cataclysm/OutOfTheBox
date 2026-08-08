// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Monitoring;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Monitoring;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OutOfTheBox.Application.Configuration;

namespace OutOfTheBox.UnitTests.Infrastructure.Monitoring;

/// <summary>
/// Exercises <see cref="HostResourceSamplerService.TickAsync"/> directly (one cycle, not the real
/// <see cref="PeriodicTimer"/>-driven loop) against a real SQLite <c>:memory:</c>-backed
/// <see cref="IRunRepository"/>/<see cref="IRunResourceSampleRepository"/> - covers task 14.16.
/// </summary>
public sealed class HostResourceSamplerServiceTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly ServiceProvider _scopeFactoryProvider;

    public HostResourceSamplerServiceTests()
    {
        var services = new ServiceCollection();
        services.AddTransient<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));
        services.AddTransient<IRunResourceSampleRepository>(_ => new EfRunResourceSampleRepository(_dbContextFactory.CreateContext()));
        _scopeFactoryProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _scopeFactoryProvider.Dispose();
        _dbContextFactory.Dispose();
    }

    [Fact]
    public async Task TickAsync_writes_one_sample_row_for_each_tracked_run()
    {
        var runId = Guid.NewGuid();
        var snapshot = new ResourceSnapshot(
            DateTimeOffset.UtcNow,
            new HostResourceSample(10, [10], 1000, 500, 100),
            [new RunResourceAggregate(runId, 25, 2000, [])]);

        var service = CreateService(new FakeResourceSampler(snapshot));
        await service.TickAsync(CancellationToken.None);

        var sampleRepository = new EfRunResourceSampleRepository(_dbContextFactory.CreateContext());
        var series = await sampleRepository.GetSeriesAsync(runId, CancellationToken.None);

        var sample = Assert.Single(series);
        Assert.Equal(25, sample.CpuPercent);
        Assert.Equal(2000, sample.RamBytes);
    }

    [Fact]
    public async Task TickAsync_writes_one_sample_per_tick_across_repeated_calls()
    {
        var runId = Guid.NewGuid();
        var fakeSampler = new FakeResourceSampler(new ResourceSnapshot(
            DateTimeOffset.UtcNow,
            new HostResourceSample(10, [10], 1000, 500, 100),
            [new RunResourceAggregate(runId, 25, 2000, [])]));

        var service = CreateService(fakeSampler);

        await service.TickAsync(CancellationToken.None);
        fakeSampler.Snapshot = fakeSampler.Snapshot with { Timestamp = fakeSampler.Snapshot.Timestamp.AddSeconds(3) };
        await service.TickAsync(CancellationToken.None);

        var sampleRepository = new EfRunResourceSampleRepository(_dbContextFactory.CreateContext());
        var series = await sampleRepository.GetSeriesAsync(runId, CancellationToken.None);

        Assert.Equal(2, series.Count);
    }

    [Fact]
    public async Task TickAsync_tags_an_in_flight_transfer_with_that_ticks_host_level_figures()
    {
        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var transferId = Guid.NewGuid();
        await runRepository.AddAsync(new Run
        {
            Id = transferId,
            Kind = RunKind.ArtifactTransfer,
            RepoPath = @"C:\repos\example",
            ArtifactPath = "file.bin",
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        // TotalRamBytes=1000, AvailableRamBytes=200 -> used RAM 800, per the "used, not total"
        // fix - a transfer has no process tree of its own, so it's tagged with this host figure.
        var hostSample = new HostResourceSample(42, [42], 1000, 200, 50);
        var service = CreateService(new FakeResourceSampler(new ResourceSnapshot(DateTimeOffset.UtcNow, hostSample, [])));

        await service.TickAsync(CancellationToken.None);

        var sampleRepository = new EfRunResourceSampleRepository(_dbContextFactory.CreateContext());
        var series = await sampleRepository.GetSeriesAsync(transferId, CancellationToken.None);

        var sample = Assert.Single(series);
        Assert.Equal(42, sample.CpuPercent);
        Assert.Equal(800, sample.RamBytes);
    }

    [Fact]
    public async Task TickAsync_does_not_tag_a_repository_delete_with_host_figures()
    {
        // Per §11's RunResourceSample entity note ("used by every run kind that gets one - not
        // deletes") - a delete has no process tree AND isn't a transfer, so it must not receive a
        // host-tagged sample the way an in-flight transfer does.
        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var deleteId = Guid.NewGuid();
        await runRepository.AddAsync(new Run
        {
            Id = deleteId,
            Kind = RunKind.RepositoryDelete,
            RepoPath = @"C:\repos\example",
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var service = CreateService(new FakeResourceSampler(new ResourceSnapshot(DateTimeOffset.UtcNow, new HostResourceSample(10, [10], 1000, 500, 100), [])));
        await service.TickAsync(CancellationToken.None);

        var sampleRepository = new EfRunResourceSampleRepository(_dbContextFactory.CreateContext());
        Assert.Empty(await sampleRepository.GetSeriesAsync(deleteId, CancellationToken.None));
    }

    private HostResourceSamplerService CreateService(IResourceSampler sampler) => new(
        Options.Create(new ServiceOptions { ResourceSamplerIntervalSeconds = 3 }),
        sampler,
        new InMemoryResourceEventBus(),
        new InMemoryRunEventBus(),
        new ResourceHistoryBuffer(new SystemClock()),
        _scopeFactoryProvider.GetRequiredService<IServiceScopeFactory>());

    private sealed class FakeResourceSampler(ResourceSnapshot snapshot) : IResourceSampler
    {
        public ResourceSnapshot Snapshot { get; set; } = snapshot;

        public Task<ResourceSnapshot> SampleAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    }
}
