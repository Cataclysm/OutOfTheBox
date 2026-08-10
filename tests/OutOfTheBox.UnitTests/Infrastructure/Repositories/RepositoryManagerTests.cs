// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

/// <summary>
/// Exercises <see cref="RepositoryManager"/>'s rejection paths (invalid name, already-exists,
/// busy) and <c>DeleteAsync</c>/<c>RenameAsync</c> end to end against a real temp directory tree -
/// none of these need a real <c>git.exe</c> invocation, since they all return before
/// <see cref="RepositoryManager.CloneAsync"/> would start one. A successful clone is covered by
/// <c>RepositoryManagement.feature</c> instead (per this project's "no real process spawning in
/// UnitTests" convention), against a real local git source the same way <c>git-command-execution</c>
/// itself is tested.
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

        var result = await manager.CloneAsync("https://example.com/repository.git", @"..\evil", null, CancellationToken.None);

        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
    }

    [Fact]
    public async Task CloneAsync_rejects_a_name_that_already_exists_and_records_a_history_row()
    {
        Directory.CreateDirectory(Path.Combine(_root, "existing-repository"));
        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var manager = CreateManager(new RunRegistry(), runRepository);

        var result = await manager.CloneAsync("https://example.com/repository.git", "existing-repository", null, CancellationToken.None);

        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.AlreadyExists, rejected.Reason);

        var recorded = await runRepository.ListAsync(new RunQuery { Kinds = [RunKind.RepositoryClone] }, CancellationToken.None);
        var row = Assert.Single(recorded);
        Assert.Equal(RunOutcome.AlreadyExists, row.Outcome);
        Assert.Equal("https://example.com/repository.git", row.SourceUrl);
    }

    [Fact]
    public async Task CloneAsync_is_rejected_as_busy_when_the_target_is_already_locked()
    {
        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();
        var conflictingRunId = Guid.NewGuid();
        registry.TryAcquire(Path.Combine(_root, "target-repository"), conflictingRunId, cts, out _);

        var manager = CreateManager(registry);

        var result = await manager.CloneAsync("https://example.com/repository.git", "target-repository", null, CancellationToken.None);

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
        var repositoryPath = Path.Combine(_root, "busy-repository");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(Path.Combine(repositoryPath, "file.txt"), "content");

        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();
        var conflictingRunId = Guid.NewGuid();
        registry.TryAcquire(repositoryPath, conflictingRunId, cts, out _);

        var manager = CreateManager(registry);

        var result = await manager.DeleteAsync("busy-repository", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.Busy, rejected.Reason);
        Assert.Equal(conflictingRunId, rejected.ConflictingRunId);
        Assert.True(Directory.Exists(repositoryPath));
        Assert.True(File.Exists(Path.Combine(repositoryPath, "file.txt")));
    }

    [Fact]
    public async Task DeleteAsync_removes_an_idle_repository_and_records_a_completed_history_row()
    {
        var repositoryPath = Path.Combine(_root, "idle-repository");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(Path.Combine(repositoryPath, "file.txt"), "content");

        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var statsCache = new RepositoryStatsCache();
        statsCache.Set("idle-repository", new RepositoryStats(1, false, null, false, null, null));

        var manager = CreateManager(new RunRegistry(), runRepository, statsCache);

        var result = await manager.DeleteAsync("idle-repository", CancellationToken.None);

        Assert.IsType<RepositoryActionResult.Accepted>(result);
        Assert.False(Directory.Exists(repositoryPath));
        Assert.Null(statsCache.TryGet("idle-repository"));

        var recorded = await runRepository.ListAsync(new RunQuery { Kinds = [RunKind.RepositoryDelete] }, CancellationToken.None);
        var row = Assert.Single(recorded);
        Assert.Equal(RunOutcome.Completed, row.Outcome);
        Assert.NotNull(row.CompletedAt);
    }

    [Fact]
    public async Task DeleteAsync_retries_and_succeeds_once_a_transient_file_lock_clears()
    {
        // Reproduces the real-machine report this retry was added for: a file briefly still open
        // (there, believed to be an AV/indexer handle release race after the contents were already
        // cleared) makes the first delete attempt(s) fail, but the operation succeeds without the
        // operator needing to manually retry, once the lock actually clears within the retry window.
        var repositoryPath = Path.Combine(_root, "transiently-locked-repository");
        Directory.CreateDirectory(repositoryPath);
        var filePath = Path.Combine(repositoryPath, "file.txt");
        File.WriteAllText(filePath, "content");

        var lockingStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await lockingStream.DisposeAsync();
        });

        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var manager = CreateManager(new RunRegistry(), runRepository);

        var result = await manager.DeleteAsync("transiently-locked-repository", CancellationToken.None);

        Assert.IsType<RepositoryActionResult.Accepted>(result);
        Assert.False(Directory.Exists(repositoryPath));

        var recorded = await runRepository.ListAsync(new RunQuery { Kinds = [RunKind.RepositoryDelete] }, CancellationToken.None);
        var row = Assert.Single(recorded);
        Assert.Equal(RunOutcome.Completed, row.Outcome);
    }

    [Fact]
    public async Task DeleteAsync_fails_cleanly_when_a_file_stays_locked_past_the_retry_window()
    {
        var repositoryPath = Path.Combine(_root, "permanently-locked-repository");
        Directory.CreateDirectory(repositoryPath);
        var filePath = Path.Combine(repositoryPath, "file.txt");
        File.WriteAllText(filePath, "content");

        await using var lockingStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var manager = CreateManager(new RunRegistry(), runRepository);

        var result = await manager.DeleteAsync("permanently-locked-repository", CancellationToken.None);

        Assert.IsType<RepositoryActionResult.Accepted>(result);
        Assert.True(Directory.Exists(repositoryPath));

        var recorded = await runRepository.ListAsync(new RunQuery { Kinds = [RunKind.RepositoryDelete] }, CancellationToken.None);
        var row = Assert.Single(recorded);
        Assert.Equal(RunOutcome.Failed, row.Outcome);
        Assert.False(string.IsNullOrEmpty(row.Stderr));
    }

    [Fact]
    public async Task RenameAsync_rejects_a_name_that_escapes_the_root()
    {
        var manager = CreateManager(new RunRegistry());

        var result = await manager.RenameAsync(@"..\evil", "new-name", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
    }

    [Fact]
    public async Task RenameAsync_rejects_a_new_name_that_escapes_the_root()
    {
        var repositoryPath = Path.Combine(_root, "idle-repository");
        Directory.CreateDirectory(repositoryPath);
        var manager = CreateManager(new RunRegistry());

        var result = await manager.RenameAsync("idle-repository", @"..\evil", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
        Assert.True(Directory.Exists(repositoryPath));
    }

    [Fact]
    public async Task RenameAsync_rejects_a_nonexistent_repository()
    {
        var manager = CreateManager(new RunRegistry());

        var result = await manager.RenameAsync("does-not-exist", "new-name", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.NotFound, rejected.Reason);
    }

    [Fact]
    public async Task RenameAsync_rejects_when_the_new_name_already_exists()
    {
        Directory.CreateDirectory(Path.Combine(_root, "source-repository"));
        Directory.CreateDirectory(Path.Combine(_root, "existing-repository"));
        var manager = CreateManager(new RunRegistry());

        var result = await manager.RenameAsync("source-repository", "existing-repository", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.AlreadyExists, rejected.Reason);
        Assert.True(Directory.Exists(Path.Combine(_root, "source-repository")));
    }

    [Fact]
    public async Task RenameAsync_is_rejected_as_busy_and_leaves_the_repository_untouched()
    {
        var repositoryPath = Path.Combine(_root, "busy-repository");
        Directory.CreateDirectory(repositoryPath);

        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();
        var conflictingRunId = Guid.NewGuid();
        registry.TryAcquire(repositoryPath, conflictingRunId, cts, out _);

        var manager = CreateManager(registry);

        var result = await manager.RenameAsync("busy-repository", "new-name", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.Busy, rejected.Reason);
        Assert.Equal(conflictingRunId, rejected.ConflictingRunId);
        Assert.True(Directory.Exists(repositoryPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "new-name")));
    }

    [Fact]
    public async Task RenameAsync_moves_an_idle_repository_to_the_new_name_and_clears_the_old_stats_cache_entry()
    {
        var oldPath = Path.Combine(_root, "old-name");
        Directory.CreateDirectory(oldPath);
        File.WriteAllText(Path.Combine(oldPath, "file.txt"), "content");

        var statsCache = new RepositoryStatsCache();
        statsCache.Set("old-name", new RepositoryStats(1, false, null, false, null, null));

        var manager = CreateManager(new RunRegistry(), statsCache: statsCache);

        var result = await manager.RenameAsync("old-name", "new-name", CancellationToken.None);

        Assert.IsType<RepositoryGitActionResult.Succeeded>(result);
        Assert.False(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(Path.Combine(_root, "new-name")));
        Assert.True(File.Exists(Path.Combine(_root, "new-name", "file.txt")));
        Assert.Null(statsCache.TryGet("old-name"));
    }

    [Fact]
    public async Task RenameAsync_repoints_existing_run_history_so_the_clone_source_url_still_resolves_under_the_new_name()
    {
        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var oldPath = Path.Combine(_root, "old-name");
        Directory.CreateDirectory(oldPath);

        await runRepository.AddAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.RepositoryClone,
            RepositoryPath = oldPath,
            SourceUrl = "https://example.com/original.git",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-9),
            Outcome = RunOutcome.Completed,
        }, CancellationToken.None);

        var manager = CreateManager(new RunRegistry(), runRepository);

        var result = await manager.RenameAsync("old-name", "new-name", CancellationToken.None);

        Assert.IsType<RepositoryGitActionResult.Succeeded>(result);
        Assert.Equal("https://example.com/original.git", await manager.GetCloneSourceUrlAsync("new-name", CancellationToken.None));
        Assert.Null(await manager.GetCloneSourceUrlAsync("old-name", CancellationToken.None));
    }

    [Fact]
    public async Task GitActions_reject_a_name_that_escapes_the_root()
    {
        var manager = CreateManager(new RunRegistry());

        foreach (var operation in GitActionOperations(manager))
        {
            var result = await operation(@"..\evil");
            var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
            Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
        }
    }

    [Fact]
    public async Task GitActions_reject_a_nonexistent_repository()
    {
        var manager = CreateManager(new RunRegistry());

        foreach (var operation in GitActionOperations(manager))
        {
            var result = await operation("does-not-exist");
            var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
            Assert.Equal(RepositoryActionRejectionReason.NotFound, rejected.Reason);
        }
    }

    [Fact]
    public async Task GitActions_reject_a_busy_repository_and_never_invoke_git()
    {
        var repositoryPath = Path.Combine(_root, "busy-repository");
        Directory.CreateDirectory(repositoryPath);

        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();
        var conflictingRunId = Guid.NewGuid();
        registry.TryAcquire(repositoryPath, conflictingRunId, cts, out _);

        // CreateManager's UnreachableProcessRunner fails the test loudly if any of these reach
        // process execution instead of being rejected up front.
        var manager = CreateManager(registry);

        foreach (var operation in GitActionOperations(manager))
        {
            var result = await operation("busy-repository");
            var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
            Assert.Equal(RepositoryActionRejectionReason.Busy, rejected.Reason);
            Assert.Equal(conflictingRunId, rejected.ConflictingRunId);
        }
    }

    [Fact]
    public async Task SwitchBranchAsync_rejects_a_name_that_escapes_the_root()
    {
        var manager = CreateManager(new RunRegistry());

        var result = await manager.SwitchBranchAsync(@"..\evil", "main", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
    }

    [Fact]
    public async Task SwitchBranchAsync_rejects_a_nonexistent_repository()
    {
        var manager = CreateManager(new RunRegistry());

        var result = await manager.SwitchBranchAsync("does-not-exist", "main", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.NotFound, rejected.Reason);
    }

    [Fact]
    public async Task CheckoutCommitAsync_rejects_a_name_that_escapes_the_root()
    {
        var manager = CreateManager(new RunRegistry());

        var result = await manager.CheckoutCommitAsync(@"..\evil", "abc123", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
    }

    [Fact]
    public async Task CheckoutCommitAsync_rejects_a_nonexistent_repository()
    {
        var manager = CreateManager(new RunRegistry());

        var result = await manager.CheckoutCommitAsync("does-not-exist", "abc123", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.NotFound, rejected.Reason);
    }

    [Fact]
    public async Task CheckoutCommitAsync_rejects_a_busy_repository_and_never_invokes_git()
    {
        var repositoryPath = Path.Combine(_root, "busy-repository");
        Directory.CreateDirectory(repositoryPath);

        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();
        var conflictingRunId = Guid.NewGuid();
        registry.TryAcquire(repositoryPath, conflictingRunId, cts, out _);

        var manager = CreateManager(registry);

        var result = await manager.CheckoutCommitAsync("busy-repository", "abc123", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryGitActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.Busy, rejected.Reason);
        Assert.Equal(conflictingRunId, rejected.ConflictingRunId);
    }

    [Fact]
    public async Task ListCommitsAsync_returns_empty_for_a_name_that_escapes_the_root()
    {
        var manager = CreateManager(new RunRegistry());

        Assert.Empty(await manager.ListCommitsAsync(@"..\evil", 0, 50, CancellationToken.None));
    }

    [Fact]
    public async Task ListCommitsAsync_returns_empty_for_a_nonexistent_repository()
    {
        var manager = CreateManager(new RunRegistry());

        Assert.Empty(await manager.ListCommitsAsync("does-not-exist", 0, 50, CancellationToken.None));
    }

    [Fact]
    public async Task GetCloneSourceUrlAsync_returns_null_for_a_name_that_escapes_the_root()
    {
        var manager = CreateManager(new RunRegistry());

        Assert.Null(await manager.GetCloneSourceUrlAsync(@"..\evil", CancellationToken.None));
    }

    [Fact]
    public async Task GetCloneSourceUrlAsync_returns_null_when_no_clone_history_exists()
    {
        var repositoryPath = Path.Combine(_root, "pre-existing-repository");
        Directory.CreateDirectory(repositoryPath);
        var manager = CreateManager(new RunRegistry());

        Assert.Null(await manager.GetCloneSourceUrlAsync("pre-existing-repository", CancellationToken.None));
    }

    [Fact]
    public async Task GetCloneSourceUrlAsync_returns_the_most_recent_completed_clones_source_url()
    {
        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var repositoryPath = Path.Combine(_root, "cloned-repository");
        Directory.CreateDirectory(repositoryPath);

        await runRepository.AddAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.RepositoryClone,
            RepositoryPath = repositoryPath,
            SourceUrl = "https://example.com/old.git",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-9),
            Outcome = RunOutcome.Completed,
        }, CancellationToken.None);
        await runRepository.AddAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.RepositoryClone,
            RepositoryPath = repositoryPath,
            SourceUrl = "https://example.com/new.git",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Completed,
        }, CancellationToken.None);

        var manager = CreateManager(new RunRegistry(), runRepository);

        Assert.Equal("https://example.com/new.git", await manager.GetCloneSourceUrlAsync("cloned-repository", CancellationToken.None));
    }

    [Fact]
    public async Task ListBranchesAsync_returns_empty_for_a_nonexistent_repository()
    {
        var manager = CreateManager(new RunRegistry());

        Assert.Empty(await manager.ListBranchesAsync("does-not-exist", CancellationToken.None));
    }

    private static IEnumerable<Func<string, Task<RepositoryGitActionResult>>> GitActionOperations(IRepositoryManager manager) =>
    [
        name => manager.PullAsync(name, CancellationToken.None),
        name => manager.PushAsync(name, CancellationToken.None),
        name => manager.ForcePushAsync(name, CancellationToken.None),
        name => manager.FetchAsync(name, CancellationToken.None),
        name => manager.CleanAsync(name, CancellationToken.None),
    ];

    private RepositoryManager CreateManager(RunRegistry runRegistry, IRunRepository? runRepository = null, RepositoryStatsCache? statsCache = null) =>
        new(
            new WorkingDirectoryResolver(Options.Create(new ServiceOptions { RootDirectory = _root }), NullLogger<WorkingDirectoryResolver>.Instance),
            runRegistry,
            runRepository ?? new EfRunRepository(_dbContextFactory.CreateContext()),
            new NoOpRunEventBus(),
            new UnreachableProcessRunner(),
            new UnreachableStatsProvider(),
            statsCache ?? new RepositoryStatsCache(),
            new NoOpRepositoryStatsEventBus(),
            new NoOpGitCredentialStore(),
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ServiceOptions
            {
                RootDirectory = _root,
                DefaultExecutionTimeoutSeconds = 5,
                OutputCapBytes = 1024 * 1024,
            }),
            NullLogger<RepositoryManager>.Instance);

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

    private sealed class NoOpRepositoryStatsEventBus : IRepositoryStatsEventBus
    {
        public void Publish(string repositoryName)
        {
        }

        public IDisposable Subscribe(Action<string> handler) => new NoOpSubscription();

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
        public Task<RepositoryStats> ComputeAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A rejection-path test unexpectedly reached stats computation.");

        public Task<GitStatusSnapshot> ComputeGitStatusAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A rejection-path test unexpectedly reached stats computation.");

        public Task<long> ComputeSizeAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A rejection-path test unexpectedly reached stats computation.");
    }

    /// <summary>No credential ever needs attention - none of the scenarios here exercise git operations that would record an outcome.</summary>
    private sealed class NoOpGitCredentialStore : IGitCredentialStore
    {
        public Task<GitCredentialAuthorizeResult> AuthorizeAsync(string host, string token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A rejection-path test unexpectedly reached credential authorization.");

        public Task<IReadOnlyList<GitHostAuthorizationSummary>> ListAuthorizedHostsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GitHostAuthorizationSummary>>([]);

        public Task<GitCredentialRevokeResult> RevokeAsync(string host, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A rejection-path test unexpectedly reached credential revocation.");

        public Task RecordOutcomeAsync(string host, bool succeeded, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<GitHostCredentialHealth?> GetHealthAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<GitHostCredentialHealth?>(null);
    }
}
