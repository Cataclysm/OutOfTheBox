// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

/// <summary>
/// Covers <see cref="RepositoryFetchSampler.FetchAllOnceAsync"/> directly rather than the real
/// <see cref="PeriodicTimer"/>-driven loop, the same pattern <see cref="RepositoryStatsSamplerTests"/>
/// uses for its own sampler: a non-git directory must never be fetched, and one repository's fetch
/// throwing unexpectedly must not stop the sweep for the rest, matching this sampler family's shared
/// crash-resilience requirement (an uncaught exception in a <c>BackgroundService</c> stops the whole
/// host by default).
/// </summary>
public sealed class RepositoryFetchSamplerTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly FakeRepositoryManager _fakeManager = new();

    public RepositoryFetchSamplerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepositoryManager>(_fakeManager);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose() => _serviceProvider.Dispose();

    [Fact]
    public async Task FetchAllOnceAsync_fetches_only_git_repositories_and_survives_one_throwing()
    {
        _fakeManager.Summaries =
        [
            new RepositorySummary { Name = "repo-a", Path = "a", StatsComputed = true, IsGitRepository = true, IsActive = false },
            new RepositorySummary { Name = "repo-b", Path = "b", StatsComputed = true, IsGitRepository = true, IsActive = false },
            new RepositorySummary { Name = "not-git", Path = "c", StatsComputed = true, IsGitRepository = false, IsActive = false },
        ];
        _fakeManager.ThrowingNames.Add("repo-a");

        var sampler = new RepositoryFetchSampler(
            Options.Create(new ServiceOptions()),
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance),
            NullLogger<RepositoryFetchSampler>.Instance);

        // Would previously never return at all for "repo-a" the way an unhandled exception in a real
        // BackgroundService.ExecuteAsync loop wouldn't - here it must simply complete, having still
        // attempted (and recorded an attempt for) "repo-b".
        await sampler.FetchAllOnceAsync(CancellationToken.None);

        Assert.Equal(["repo-a", "repo-b"], _fakeManager.FetchedNames);
    }

    [Fact]
    public async Task FetchAllOnceAsync_does_not_throw_when_a_fetch_reports_a_failed_result()
    {
        _fakeManager.Summaries = [new RepositorySummary { Name = "repo-a", Path = "a", StatsComputed = true, IsGitRepository = true, IsActive = false }];
        _fakeManager.FailingNames.Add("repo-a");

        var sampler = new RepositoryFetchSampler(
            Options.Create(new ServiceOptions()),
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance),
            NullLogger<RepositoryFetchSampler>.Instance);

        await sampler.FetchAllOnceAsync(CancellationToken.None);

        Assert.Equal(["repo-a"], _fakeManager.FetchedNames);
    }

    [Fact]
    public async Task ExecuteAsync_waits_for_the_application_to_finish_starting_before_its_first_sweep()
    {
        _fakeManager.Summaries = [new RepositorySummary { Name = "repo-a", Path = "a", StatsComputed = true, IsGitRepository = true, IsActive = false }];

        var lifetime = new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance);

        // An interval long enough that, within this test's timeout, only the immediate "run once as
        // soon as the app has started" sweep could plausibly have fired - a real PeriodicTimer tick
        // never gets the chance to.
        var sampler = new RepositoryFetchSampler(
            Options.Create(new ServiceOptions { RepositoryFetchIntervalSeconds = 3600 }),
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            lifetime,
            NullLogger<RepositoryFetchSampler>.Instance);

        await sampler.StartAsync(CancellationToken.None);
        try
        {
            // ExecuteAsync is blocked waiting for ApplicationStarted at this point - nothing should
            // have been fetched yet, however briefly we give it to reach that wait.
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            Assert.Empty(_fakeManager.FetchedNames);

            lifetime.NotifyStarted();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_fakeManager.FetchedNames.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            Assert.Equal(["repo-a"], _fakeManager.FetchedNames);
        }
        finally
        {
            await sampler.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FakeRepositoryManager : IRepositoryManager
    {
        public IReadOnlyList<RepositorySummary> Summaries { get; set; } = [];

        public List<string> FetchedNames { get; } = [];

        public HashSet<string> ThrowingNames { get; } = [];

        public HashSet<string> FailingNames { get; } = [];

        public Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(Summaries);

        public Task<RepositoryGitActionResult> FetchAsync(string name, CancellationToken cancellationToken)
        {
            FetchedNames.Add(name);

            if (ThrowingNames.Contains(name))
            {
                throw new InvalidOperationException("Simulated unexpected fetch failure.");
            }

            return Task.FromResult<RepositoryGitActionResult>(FailingNames.Contains(name)
                ? new RepositoryGitActionResult.Failed("Simulated git failure.")
                : new RepositoryGitActionResult.Succeeded());
        }

        public Task<RepositoryActionResult> CloneAsync(string url, string name, string? branch, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<RepositoryActionResult> DeleteAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<RepositoryGitActionResult> PullAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<RepositoryGitActionResult> PushAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<RepositoryGitActionResult> ForcePushAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<RepositoryGitActionResult> CleanAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<RepositoryGitActionResult> RenameAsync(string name, string newName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<string?> GetCloneSourceUrlAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<IReadOnlyList<RepositoryBranch>> ListBranchesAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<RepositoryGitActionResult> SwitchBranchAsync(string name, string branch, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<IReadOnlyList<string>> ListRemoteBranchesAsync(string url, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<IReadOnlyList<CommitSummary>> ListCommitsAsync(string name, int skip, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<RepositoryGitActionResult> CheckoutCommitAsync(string name, string hash, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<CommitDetail?> GetCommitDetailAsync(string name, string hash, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<string?> GetCommitFileDiffAsync(string name, string hash, string relativePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<IReadOnlyList<string>> ListDirtyFilePathsAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }
}
