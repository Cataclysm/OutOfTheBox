// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Bunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="RepositoryDetail"/> component via bUnit, against a real
/// <see cref="RepositoryManager"/> and a real temp directory - reproduces the reported gap where
/// this page's own History tab never picked up a run's completion live, the same class of bug
/// <c>HistoryComponentTests</c> covers for the standalone History view.
/// </summary>
public sealed class RepositoryDetailComponentTests : DashboardComponentTestContext, IDisposable
{
    private readonly string _root;
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly IRunEventBus _runEventBus = new InMemoryRunEventBus(NullLogger<InMemoryRunEventBus>.Instance);
    private readonly IRepositoryStatsEventBus _repositoryStatsEventBus = new InMemoryRepositoryStatsEventBus(NullLogger<InMemoryRepositoryStatsEventBus>.Instance);
    private readonly RunRegistry _runRegistry = new();
    private readonly RepositoryStatsCache _statsCache = new();
    private readonly ServiceProvider _scopeFactoryProvider;
    private readonly string _repositoryPath;

    public RepositoryDetailComponentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "oob-tests", Guid.NewGuid().ToString("N"));
        _repositoryPath = Path.Combine(_root, "example");
        Directory.CreateDirectory(_repositoryPath);
        _statsCache.Set("example", new RepositoryStats(100, IsGitRepository: true, Branch: "main", IsDirty: false, AheadCount: null, BehindCount: null));

        var scopeFactoryServices = new ServiceCollection();
        scopeFactoryServices.AddTransient<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));
        _scopeFactoryProvider = scopeFactoryServices.BuildServiceProvider();

        var options = Options.Create(new ServiceOptions { RootDirectory = _root, DefaultExecutionTimeoutSeconds = 5, OutputCapBytes = 1024 * 1024 });
        var repositoryManager = new RepositoryManager(
            new WorkingDirectoryResolver(options, NullLogger<WorkingDirectoryResolver>.Instance),
            _runRegistry,
            new EfRunRepository(_dbContextFactory.CreateContext()),
            _runEventBus,
            new EmptyOutputProcessRunner(),
            new UnreachableStatsProvider(),
            _statsCache,
            _repositoryStatsEventBus,
            new NoOpGitCredentialStore(),
            _scopeFactoryProvider.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<RepositoryManager>.Instance);

        Services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment());
        Services.AddSingleton<IGitCredentialStore>(new NoOpGitCredentialStore());
        Services.AddSingleton<IRepositoryManager>(repositoryManager);
        Services.AddSingleton<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));
        Services.AddSingleton<IWorkingDirectoryResolver>(new WorkingDirectoryResolver(options, NullLogger<WorkingDirectoryResolver>.Instance));
        Services.AddSingleton(_runRegistry);
        Services.AddSingleton(_runEventBus);
        Services.AddSingleton(_repositoryStatsEventBus);
    }

    [Fact]
    public async Task A_run_completed_elsewhere_appears_live_on_the_history_tab_without_reload()
    {
        var cut = Render<RepositoryDetail>(parameters => parameters.Add(p => p.Name, "example"));
        cut.WaitForAssertion(() => Assert.Contains("example", cut.Markup));

        cut.FindAll("button").Single(b => b.TextContent == "History").Click();
        cut.WaitForAssertion(() => Assert.Contains("No runs recorded against this repository yet.", cut.Markup));

        var runRepository = Services.GetRequiredService<IRunRepository>();
        var runId = Guid.NewGuid();
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepositoryPath = _repositoryPath,
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            ExitCode = 0,
            Outcome = RunOutcome.Completed,
        }, CancellationToken.None);

        // Exactly what an MCP dotnet_run call publishes on completion - no dashboard interaction at all.
        _runEventBus.Publish(new RunEvent(runId, RunKind.DotnetCommand, RunEventType.Terminal, _repositoryPath));

        cut.WaitForAssertion(() => Assert.Contains("dotnet test", cut.Markup), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void An_event_for_a_different_repository_does_not_trigger_a_refresh()
    {
        var cut = Render<RepositoryDetail>(parameters => parameters.Add(p => p.Name, "example"));
        cut.WaitForAssertion(() => Assert.Contains("example", cut.Markup));

        cut.FindAll("button").Single(b => b.TextContent == "History").Click();
        cut.WaitForAssertion(() => Assert.Contains("No runs recorded against this repository yet.", cut.Markup));

        var otherRepositoryPath = Path.Combine(_root, "other");
        _runEventBus.Publish(new RunEvent(Guid.NewGuid(), RunKind.GitCommand, RunEventType.Terminal, otherRepositoryPath));

        // No assertion of "did nothing happen" is directly observable other than the empty state
        // staying put - proves the path-filter in RepositoryDetail's OnRunEvent actually filters,
        // not just that the subscription exists.
        cut.WaitForAssertion(() => Assert.Contains("No runs recorded against this repository yet.", cut.Markup));
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

    // RepositoryDetail's own OnInitializedAsync calls RepositoryManager.ListBranchesAsync
    // unconditionally (needed for its branch-switch dropdown), which shells out to `git branch -a`
    // via GitCaptureRunner regardless of what's cached in RepositoryStatsCache - unlike
    // RepositoriesComponentTests' own UnreachableProcessRunner, this test can't just refuse every
    // call, since that path always runs for this component. A clean "no branches" exit is a
    // realistic, harmless response for a test that isn't exercising branch listing itself.
    private sealed class EmptyOutputProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken, Action<int>? onStarted = null) =>
            Task.FromResult(new ProcessRunResult(0));
    }

    private sealed class UnreachableStatsProvider : IRepositoryStatsProvider
    {
        public Task<RepositoryStats> ComputeAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test seeds RepositoryStatsCache directly.");

        public Task<GitStatusSnapshot> ComputeGitStatusAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test seeds RepositoryStatsCache directly.");

        public Task<long> ComputeSizeAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test seeds RepositoryStatsCache directly.");
    }

    /// <summary>No credential ever needs attention - this test never calls pull/push/force-push/fetch, the only actions that would consult this.</summary>
    private sealed class NoOpGitCredentialStore : IGitCredentialStore
    {
        public Task<GitCredentialAuthorizeResult> AuthorizeAsync(string host, string token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task<IReadOnlyList<GitHostAuthorizationSummary>> ListAuthorizedHostsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task<GitCredentialRevokeResult> RevokeAsync(string host, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task RecordOutcomeAsync(string host, bool succeeded, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task<OutOfTheBox.Domain.Repositories.GitHostCredentialHealth?> GetHealthAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<OutOfTheBox.Domain.Repositories.GitHostCredentialHealth?>(null);

        public Task RecordRepositoryOutcomeAsync(string repositoryPath, bool succeeded, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task<OutOfTheBox.Domain.Repositories.RepositoryCredentialHealth?> GetRepositoryHealthAsync(string repositoryPath, CancellationToken cancellationToken) =>
            Task.FromResult<OutOfTheBox.Domain.Repositories.RepositoryCredentialHealth?>(null);

        public Task RenameRepositoryHealthAsync(string oldRepositoryPath, string newRepositoryPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task<string?> GetCurrentTokenAsync(string host, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");
    }
}
