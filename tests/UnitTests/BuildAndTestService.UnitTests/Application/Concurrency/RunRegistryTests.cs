// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using BuildAndTestService.Application.Concurrency;

namespace BuildAndTestService.UnitTests.Application.Concurrency;

public sealed class RunRegistryTests
{
    [Fact]
    public void TryAcquire_succeeds_for_a_repo_with_no_active_run()
    {
        var registry = new RunRegistry();

        var acquired = registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), new CancellationTokenSource(), out var conflictingRunId);

        Assert.True(acquired);
        Assert.Equal(default, conflictingRunId);
    }

    [Fact]
    public void TryAcquire_succeeds_independently_for_different_repos()
    {
        var registry = new RunRegistry();

        var acquiredA = registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), new CancellationTokenSource(), out _);
        var acquiredB = registry.TryAcquire(@"C:\repos\repo-b", Guid.NewGuid(), new CancellationTokenSource(), out _);

        Assert.True(acquiredA);
        Assert.True(acquiredB);
    }

    [Fact]
    public void TryAcquire_fails_for_a_repo_already_locked_and_reports_the_holding_run_id()
    {
        var registry = new RunRegistry();
        var firstRunId = Guid.NewGuid();
        registry.TryAcquire(@"C:\repos\repo-a", firstRunId, new CancellationTokenSource(), out _);

        var acquired = registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), new CancellationTokenSource(), out var conflictingRunId);

        Assert.False(acquired);
        Assert.Equal(firstRunId, conflictingRunId);
    }

    [Fact]
    public void Release_frees_the_repo_for_a_subsequent_acquire()
    {
        var registry = new RunRegistry();
        registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), new CancellationTokenSource(), out _);

        registry.Release(@"C:\repos\repo-a");
        var acquired = registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), new CancellationTokenSource(), out _);

        Assert.True(acquired);
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
            return registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), new CancellationTokenSource(), out var conflictingRunId);
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
        registry.TryAcquire(@"C:\repos\repo-a", runId, cts, out _);

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
        registry.TryAcquire(@"C:\repos\repo-a", runId, cts, out _);
        registry.Release(@"C:\repos\repo-a");

        Assert.False(registry.TryCancel(runId));
    }

    [Fact]
    public void TryCancel_returns_false_rather_than_throwing_if_the_token_source_was_already_disposed()
    {
        var registry = new RunRegistry();
        var runId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        registry.TryAcquire(@"C:\repos\repo-a", runId, cts, out _);
        cts.Dispose();

        // Simulates the narrow race where the run finished (and its CTS was disposed) between a
        // caller looking up the run id and this Cancel() call actually happening.
        Assert.False(registry.TryCancel(runId));
    }
}
