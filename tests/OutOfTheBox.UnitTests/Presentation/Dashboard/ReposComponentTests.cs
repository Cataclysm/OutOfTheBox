// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="Repos"/> component via bUnit, against a real <see cref="RepositoryManager"/>
/// and a real temp directory tree - closes the gap tasks.md's §13 deviation notes left open for
/// 13.19 (live active/idle indicator), 13.20 (live git-status refresh), and 13.21 (filter/search),
/// the same way <c>StatusComponentTests</c> closes §12's equivalent gap.
/// </summary>
public sealed class ReposComponentTests : BunitContext, IDisposable
{
    private readonly string _root;
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly IRunEventBus _runEventBus = new InMemoryRunEventBus();
    private readonly RunRegistry _runRegistry = new();
    private readonly RepositoryStatsCache _statsCache = new();
    private readonly ServiceProvider _scopeFactoryProvider;

    public ReposComponentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "oob-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "repo-a"));
        Directory.CreateDirectory(Path.Combine(_root, "repo-b"));
        _statsCache.Set("repo-a", new RepositoryStats(100, IsGitRepository: true, Branch: "main", IsDirty: false, AheadCount: null, BehindCount: null));
        _statsCache.Set("repo-b", new RepositoryStats(200, IsGitRepository: false, Branch: null, IsDirty: false, AheadCount: null, BehindCount: null));

        var scopeFactoryServices = new ServiceCollection();
        scopeFactoryServices.AddTransient<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));
        _scopeFactoryProvider = scopeFactoryServices.BuildServiceProvider();

        var options = Options.Create(new ServiceOptions { RootDirectory = _root, DefaultExecutionTimeoutSeconds = 5, OutputCapBytes = 1024 * 1024 });
        var repositoryManager = new RepositoryManager(
            new WorkingDirectoryResolver(options),
            _runRegistry,
            new EfRunRepository(_dbContextFactory.CreateContext()),
            _runEventBus,
            new UnreachableProcessRunner(),
            new UnreachableStatsProvider(),
            _statsCache,
            _scopeFactoryProvider.GetRequiredService<IServiceScopeFactory>(),
            options);

        Services.AddSingleton<IRepositoryManager>(repositoryManager);
        Services.AddSingleton<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));
        Services.AddSingleton(_runRegistry);
        Services.AddSingleton(_runEventBus);
    }

    [Fact]
    public void Shows_repositories_as_idle_when_nothing_holds_their_lock()
    {
        var cut = Render<Repos>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("repo-a", cut.Markup);
            Assert.Contains("repo-b", cut.Markup);
            Assert.DoesNotContain("kind-badge\">Active", cut.Markup);
        });
    }

    [Fact]
    public void Active_indicator_updates_live_when_a_run_starts_and_ends_against_a_repository()
    {
        var cut = Render<Repos>();
        cut.WaitForAssertion(() => Assert.Contains("repo-a", cut.Markup));

        var repoAPath = Path.Combine(_root, "repo-a");
        var runId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        _runRegistry.TryAcquire(repoAPath, runId, cts, out _);
        _runEventBus.Publish(new RunEvent(runId, RunKind.GitCommand, RunEventType.Started, repoAPath));

        cut.WaitForAssertion(() => Assert.Contains("kind-badge\">Active", cut.Markup), TimeSpan.FromSeconds(2));

        _runRegistry.Release(repoAPath);
        _runEventBus.Publish(new RunEvent(runId, RunKind.GitCommand, RunEventType.Terminal, repoAPath));

        cut.WaitForAssertion(() => Assert.DoesNotContain("kind-badge\">Active", cut.Markup), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Git_status_refreshes_live_after_a_run_against_the_repository_completes()
    {
        var cut = Render<Repos>();
        cut.WaitForAssertion(() => Assert.Contains("main (clean)", cut.Markup));

        // Simulates what RepositoryStatsSampler's event-driven recompute does after a real `git
        // pull` completes: the cache is updated first, then the terminal event fires.
        var repoAPath = Path.Combine(_root, "repo-a");
        _statsCache.Set("repo-a", new RepositoryStats(100, IsGitRepository: true, Branch: "main", IsDirty: true, AheadCount: null, BehindCount: 2));
        _runEventBus.Publish(new RunEvent(Guid.NewGuid(), RunKind.GitCommand, RunEventType.Terminal, repoAPath));

        cut.WaitForAssertion(() => Assert.Contains("main (dirty), behind 2", cut.Markup), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Searching_by_name_narrows_the_visible_list()
    {
        var cut = Render<Repos>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("repo-a", cut.Markup);
            Assert.Contains("repo-b", cut.Markup);
        });

        var searchInput = cut.Find("input[placeholder='repository name']");
        searchInput.Input("repo-a");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("repo-a", cut.Markup);
            Assert.DoesNotContain("repo-b", cut.Markup);
        });
    }

    [Fact]
    public void Clearing_filters_restores_the_full_list()
    {
        var cut = Render<Repos>();
        cut.WaitForAssertion(() => Assert.Contains("repo-a", cut.Markup));

        cut.Find("input[placeholder='repository name']").Input("repo-a");
        cut.WaitForAssertion(() => Assert.DoesNotContain("repo-b", cut.Markup));

        var clearFiltersButton = cut.FindAll("button").Single(b => b.TextContent == "Clear filters");
        clearFiltersButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("repo-a", cut.Markup);
            Assert.Contains("repo-b", cut.Markup);
        });
    }

    /// <inheritdoc />
    public new void Dispose()
    {
        _dbContextFactory.Dispose();
        _scopeFactoryProvider.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        base.Dispose();
    }

    private sealed class UnreachableProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken, Action<int>? onStarted = null) =>
            throw new InvalidOperationException("This test never starts a real clone.");
    }

    private sealed class UnreachableStatsProvider : IRepositoryStatsProvider
    {
        public Task<RepositoryStats> ComputeAsync(string repoPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test seeds RepositoryStatsCache directly.");
    }
}
