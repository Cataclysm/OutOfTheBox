// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;

namespace OutOfTheBox.UnitTests.Application.Concurrency;

public sealed class RunRegistryTests
{
    [Fact]
    public void TryAcquire_succeeds_for_a_repo_with_no_active_run()
    {
        var registry = new RunRegistry();

        var acquired = registry.TryAcquire(@"C:\repositories\repository-a", Guid.NewGuid(), new CancellationTokenSource(), out var conflictingRunId);

        Assert.True(acquired);
        Assert.Equal(default, conflictingRunId);
    }

    [Fact]
    public void TryAcquire_succeeds_independently_for_different_repos()
    {
        var registry = new RunRegistry();

        var acquiredA = registry.TryAcquire(@"C:\repositories\repository-a", Guid.NewGuid(), new CancellationTokenSource(), out _);
        var acquiredB = registry.TryAcquire(@"C:\repositories\repository-b", Guid.NewGuid(), new CancellationTokenSource(), out _);

        Assert.True(acquiredA);
        Assert.True(acquiredB);
    }

    [Fact]
    public void TryAcquire_fails_for_a_repo_already_locked_and_reports_the_holding_run_id()
    {
        var registry = new RunRegistry();
        var firstRunId = Guid.NewGuid();
        registry.TryAcquire(@"C:\repositories\repository-a", firstRunId, new CancellationTokenSource(), out _);

        var acquired = registry.TryAcquire(@"C:\repositories\repository-a", Guid.NewGuid(), new CancellationTokenSource(), out var conflictingRunId);

        Assert.False(acquired);
        Assert.Equal(firstRunId, conflictingRunId);
    }

    [Fact]
    public void Release_frees_the_repo_for_a_subsequent_acquire()
    {
        var registry = new RunRegistry();
        registry.TryAcquire(@"C:\repositories\repository-a", Guid.NewGuid(), new CancellationTokenSource(), out _);

        registry.Release(@"C:\repositories\repository-a");
        var acquired = registry.TryAcquire(@"C:\repositories\repository-a", Guid.NewGuid(), new CancellationTokenSource(), out _);

        Assert.True(acquired);
    }

    [Fact]
    public void TryAcquire_is_shared_bidirectionally_between_dotnet_and_git_runs_against_the_same_repo()
    {
        // RunRegistry is keyed purely by resolved repository root - it has no notion of "run kind" at
        // all, so a dotnet run and a git run contend for exactly the same lock, in both
        // directions. Per specs/dotnet-command-execution's and specs/git-command-execution's
        // "shared per-repository lock" scenarios: this requires no code change here, only proof it
        // already holds - the run id is what distinguishes them, not which endpoint (POST /run vs
        // POST /run/git) called TryAcquire.
        var registry = new RunRegistry();
        var dotnetRunId = Guid.NewGuid();
        var gitRunId = Guid.NewGuid();

        var dotnetAcquired = registry.TryAcquire(@"C:\repositories\repository-a", dotnetRunId, new CancellationTokenSource(), out _);
        var gitRejected = registry.TryAcquire(@"C:\repositories\repository-a", gitRunId, new CancellationTokenSource(), out var conflictingRunId);

        Assert.True(dotnetAcquired);
        Assert.False(gitRejected);
        Assert.Equal(dotnetRunId, conflictingRunId);

        registry.Release(@"C:\repositories\repository-a");

        var gitAcquired = registry.TryAcquire(@"C:\repositories\repository-a", gitRunId, new CancellationTokenSource(), out _);
        var dotnetRejected = registry.TryAcquire(@"C:\repositories\repository-a", Guid.NewGuid(), new CancellationTokenSource(), out var secondConflictingRunId);

        Assert.True(gitAcquired);
        Assert.False(dotnetRejected);
        Assert.Equal(gitRunId, secondConflictingRunId);
    }

    [Fact]
    public async Task TryAcquire_under_concurrent_callers_for_the_same_repo_exactly_one_wins()
    {
        var registry = new RunRegistry();
        const int callerCount = 50;
        var barrier = new Barrier(callerCount);

        var results = await Task.WhenAll(Enumerable.Range(0, callerCount).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return registry.TryAcquire(@"C:\repositories\repository-a", Guid.NewGuid(), new CancellationTokenSource(), out var conflictingRunId);
        })));

        Assert.Equal(1, results.Count(acquired => acquired));
    }

    [Fact]
    public void TryCancel_returns_false_for_an_unknown_run_id()
    {
        var registry = new RunRegistry();

        Assert.False(registry.TryCancel(Guid.NewGuid()));
    }

    [Fact]
    public void TryCancel_cancels_the_registered_token_source_for_an_active_run()
    {
        var registry = new RunRegistry();
        var runId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        registry.TryAcquire(@"C:\repositories\repository-a", runId, cts, out _);

        var cancelled = registry.TryCancel(runId);

        Assert.True(cancelled);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void TryCancel_returns_false_after_the_run_has_been_released()
    {
        var registry = new RunRegistry();
        var runId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        registry.TryAcquire(@"C:\repositories\repository-a", runId, cts, out _);
        registry.Release(@"C:\repositories\repository-a");

        Assert.False(registry.TryCancel(runId));
    }

    [Fact]
    public void TryCancel_returns_false_rather_than_throwing_if_the_token_source_was_already_disposed()
    {
        var registry = new RunRegistry();
        var runId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        registry.TryAcquire(@"C:\repositories\repository-a", runId, cts, out _);
        cts.Dispose();

        // Simulates the narrow race where the run finished (and its CTS was disposed) between a
        // caller looking up the run id and this Cancel() call actually happening.
        Assert.False(registry.TryCancel(runId));
    }

    [Fact]
    public void RegisterTransfer_makes_the_run_cancellable_without_acquiring_any_repo_lock()
    {
        // Per specs/file-transfer's "Transfers do not contend for the per-repository command lock":
        // RegisterTransfer must not touch the repository-root-keyed index TryAcquire/Release use - a
        // transfer's run id being cancellable is independent of any repository being locked.
        var registry = new RunRegistry();
        var runId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        registry.RegisterTransfer(runId, cts);
        var stillAcquirable = registry.TryAcquire(@"C:\repositories\repository-a", Guid.NewGuid(), new CancellationTokenSource(), out _);
        var cancelled = registry.TryCancel(runId);

        Assert.True(stillAcquirable, "A registered transfer must not hold any repository's lock.");
        Assert.True(cancelled);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void ReleaseTransfer_removes_the_run_so_it_can_no_longer_be_cancelled()
    {
        var registry = new RunRegistry();
        var runId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        registry.RegisterTransfer(runId, cts);

        registry.ReleaseTransfer(runId);

        Assert.False(registry.TryCancel(runId));
    }

    [Fact]
    public void IsHeld_reflects_lock_state_without_side_effects()
    {
        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();

        Assert.False(registry.IsHeld(@"C:\repositories\repository-a"));

        registry.TryAcquire(@"C:\repositories\repository-a", Guid.NewGuid(), cts, out _);
        Assert.True(registry.IsHeld(@"C:\repositories\repository-a"));

        // A read-only check must not itself acquire/consume the lock - a second real TryAcquire
        // against the same repository must still see it held.
        Assert.True(registry.IsHeld(@"C:\repositories\repository-a"));

        registry.Release(@"C:\repositories\repository-a");
        Assert.False(registry.IsHeld(@"C:\repositories\repository-a"));
    }
}
