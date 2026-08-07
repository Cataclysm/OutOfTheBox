// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

/// <summary>
/// Exercises <see cref="RepositoryManager"/>'s rejection paths (invalid name, already-exists,
/// busy) and <c>DeleteAsync</c> end to end against a real temp directory tree - none of these need
/// a real <c>git.exe</c> invocation, since they all return before <see cref="RepositoryManager.CloneAsync"/>
/// would start one. A successful clone is covered by <c>RepositoryManagement.feature</c> instead
/// (per this project's "no real process spawning in UnitTests" convention), against a real local
/// git source the same way <c>git-command-execution</c> itself is tested.
/// </summary>
public sealed class RepositoryManagerTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly ServiceProvider _serviceProvider;

    public RepositoryManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "oob-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var services = new ServiceCollection();
        services.AddTransient<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _dbContextFactory.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task CloneAsync_rejects_a_name_that_escapes_the_root()
    {
        var manager = CreateManager(new RunRegistry());

        var result = await manager.CloneAsync("https://example.com/repo.git", @"..\evil", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
    }

    [Fact]
    public async Task CloneAsync_rejects_a_name_that_already_exists_and_records_a_history_row()
    {
        Directory.CreateDirectory(Path.Combine(_root, "existing-repo"));
        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var manager = CreateManager(new RunRegistry(), runRepository);

        var result = await manager.CloneAsync("https://example.com/repo.git", "existing-repo", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.AlreadyExists, rejected.Reason);

        var recorded = await runRepository.ListAsync(new RunQuery { Kinds = [RunKind.RepositoryClone] }, CancellationToken.None);
        var row = Assert.Single(recorded);
        Assert.Equal(RunOutcome.AlreadyExists, row.Outcome);
        Assert.Equal("https://example.com/repo.git", row.SourceUrl);
    }

    [Fact]
    public async Task CloneAsync_is_rejected_as_busy_when_the_target_is_already_locked()
    {
        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();
        var conflictingRunId = Guid.NewGuid();
        registry.TryAcquire(Path.Combine(_root, "target-repo"), conflictingRunId, cts, out _);

        var manager = CreateManager(registry);

        var result = await manager.CloneAsync("https://example.com/repo.git", "target-repo", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.Busy, rejected.Reason);
        Assert.Equal(conflictingRunId, rejected.ConflictingRunId);
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_name_that_escapes_the_root()
    {
        var manager = CreateManager(new RunRegistry());

        var result = await manager.DeleteAsync(@"..\evil", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_nonexistent_repository_and_records_a_history_row()
    {
        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var manager = CreateManager(new RunRegistry(), runRepository);

        var result = await manager.DeleteAsync("does-not-exist", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.NotFound, rejected.Reason);

        var recorded = await runRepository.ListAsync(new RunQuery { Kinds = [RunKind.RepositoryDelete] }, CancellationToken.None);
        var row = Assert.Single(recorded);
        Assert.Equal(RunOutcome.NotFound, row.Outcome);
    }

    [Fact]
    public async Task DeleteAsync_is_rejected_as_busy_and_leaves_the_repository_untouched()
    {
        var repoPath = Path.Combine(_root, "busy-repo");
        Directory.CreateDirectory(repoPath);
        File.WriteAllText(Path.Combine(repoPath, "file.txt"), "content");

        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();
        var conflictingRunId = Guid.NewGuid();
        registry.TryAcquire(repoPath, conflictingRunId, cts, out _);

        var manager = CreateManager(registry);

        var result = await manager.DeleteAsync("busy-repo", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.Busy, rejected.Reason);
        Assert.Equal(conflictingRunId, rejected.ConflictingRunId);
        Assert.True(Directory.Exists(repoPath));
        Assert.True(File.Exists(Path.Combine(repoPath, "file.txt")));
    }

    [Fact]
    public async Task DeleteAsync_removes_an_idle_repository_and_records_a_completed_history_row()
    {
        var repoPath = Path.Combine(_root, "idle-repo");
        Directory.CreateDirectory(repoPath);
        File.WriteAllText(Path.Combine(repoPath, "file.txt"), "content");

        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var statsCache = new RepositoryStatsCache();
        statsCache.Set("idle-repo", new RepositoryStats(1, false, null, false, null, null));

        var manager = CreateManager(new RunRegistry(), runRepository, statsCache);

        var result = await manager.DeleteAsync("idle-repo", CancellationToken.None);

        Assert.IsType<RepositoryActionResult.Accepted>(result);
        Assert.False(Directory.Exists(repoPath));
        Assert.Null(statsCache.TryGet("idle-repo"));

        var recorded = await runRepository.ListAsync(new RunQuery { Kinds = [RunKind.RepositoryDelete] }, CancellationToken.None);
        var row = Assert.Single(recorded);
        Assert.Equal(RunOutcome.Completed, row.Outcome);
        Assert.NotNull(row.CompletedAt);
    }

    private RepositoryManager CreateManager(RunRegistry runRegistry, IRunRepository? runRepository = null, RepositoryStatsCache? statsCache = null) =>
        new(
            new WorkingDirectoryResolver(Options.Create(new ServiceOptions { RootDirectory = _root })),
            runRegistry,
            runRepository ?? new EfRunRepository(_dbContextFactory.CreateContext()),
            new NoOpRunEventBus(),
            new UnreachableProcessRunner(),
            new UnreachableStatsProvider(),
            statsCache ?? new RepositoryStatsCache(),
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ServiceOptions
            {
                RootDirectory = _root,
                DefaultExecutionTimeoutSeconds = 5,
                OutputCapBytes = 1024 * 1024,
            }));

    private sealed class NoOpRunEventBus : IRunEventBus
    {
        public void Publish(RunEvent runEvent)
        {
        }

        public IDisposable Subscribe(Action<RunEvent> handler) => new NoOpSubscription();

        private sealed class NoOpSubscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>Fails the test loudly if a rejection path accidentally starts a real process - none of the scenarios here should reach this.</summary>
    private sealed class UnreachableProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken, Action<int>? onStarted = null) =>
            throw new InvalidOperationException("A rejection-path test unexpectedly reached process execution.");
    }

    private sealed class UnreachableStatsProvider : IRepositoryStatsProvider
    {
        public Task<RepositoryStats> ComputeAsync(string repoPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A rejection-path test unexpectedly reached stats computation.");
    }
}
