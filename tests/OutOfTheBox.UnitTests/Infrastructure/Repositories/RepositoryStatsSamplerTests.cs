// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

/// <summary>
/// Regression coverage for the bug that left repository stats stuck forever: one repository's
/// computation failing must not stop the sweep for every other repository, let alone crash the whole
/// <see cref="RepositoryStatsSampler"/> <c>BackgroundService</c> (which, left unhandled, stops the
/// entire host by default). Exercises <see cref="RepositoryStatsSampler.RecomputeAllOnceAsync"/>
/// directly rather than the real <see cref="PeriodicTimer"/>-driven loop, the same pattern
/// <c>HostResourceSamplerServiceTests</c> uses for its own sampler.
/// </summary>
public sealed class RepositoryStatsSamplerTests : IDisposable
{
    private readonly string _root;

    public RepositoryStatsSamplerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "oob-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "bad-repo"));
        Directory.CreateDirectory(Path.Combine(_root, "good-repo"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task RecomputeAllOnceAsync_skips_a_failing_repository_and_still_computes_the_rest()
    {
        var statsCache = new RepositoryStatsCache();
        var statsEventBus = new InMemoryRepositoryStatsEventBus(NullLogger<InMemoryRepositoryStatsEventBus>.Instance);
        var publishedNames = new List<string>();
        using var subscription = statsEventBus.Subscribe(publishedNames.Add);

        var sampler = new RepositoryStatsSampler(
            Options.Create(new ServiceOptions { RootDirectory = _root }),
            new ThrowsForOneRepositoryStatsProvider("bad-repo"),
            statsCache,
            new InMemoryRunEventBus(NullLogger<InMemoryRunEventBus>.Instance),
            statsEventBus,
            NullLogger<RepositoryStatsSampler>.Instance);

        // Would previously never return at all for "bad-repo" the way an unhandled exception in a
        // real BackgroundService.ExecuteAsync loop wouldn't - here it must simply complete.
        await sampler.RecomputeAllOnceAsync(CancellationToken.None);

        Assert.Null(statsCache.TryGet("bad-repo"));
        Assert.NotNull(statsCache.TryGet("good-repo"));
        Assert.DoesNotContain("bad-repo", publishedNames);
        Assert.Contains("good-repo", publishedNames);
    }

    private sealed class ThrowsForOneRepositoryStatsProvider(string failingRepositoryName) : IRepositoryStatsProvider
    {
        public Task<RepositoryStats> ComputeAsync(string repositoryPath, CancellationToken cancellationToken)
        {
            if (Path.GetFileName(repositoryPath) == failingRepositoryName)
            {
                throw new InvalidOperationException("Simulated stats-computation failure.");
            }

            return Task.FromResult(new RepositoryStats(1, IsGitRepository: false, Branch: null, IsDirty: false, AheadCount: null, BehindCount: null));
        }

        public Task<GitStatusSnapshot> ComputeGitStatusAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test only exercises the combined ComputeAsync path.");

        public Task<long> ComputeSizeAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test only exercises the combined ComputeAsync path.");
    }
}
