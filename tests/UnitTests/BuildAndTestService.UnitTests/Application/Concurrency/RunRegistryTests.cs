// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using BuildAndTestService.Application.Concurrency;

namespace BuildAndTestService.UnitTests.Application.Concurrency;

public sealed class RunRegistryTests
{
    [Fact]
    public void TryAcquire_succeeds_for_a_repo_with_no_active_run()
    {
        var registry = new RunRegistry();

        var acquired = registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), out var conflictingRunId);

        Assert.True(acquired);
        Assert.Equal(default, conflictingRunId);
    }

    [Fact]
    public void TryAcquire_succeeds_independently_for_different_repos()
    {
        var registry = new RunRegistry();

        var acquiredA = registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), out _);
        var acquiredB = registry.TryAcquire(@"C:\repos\repo-b", Guid.NewGuid(), out _);

        Assert.True(acquiredA);
        Assert.True(acquiredB);
    }

    [Fact]
    public void TryAcquire_fails_for_a_repo_already_locked_and_reports_the_holding_run_id()
    {
        var registry = new RunRegistry();
        var firstRunId = Guid.NewGuid();
        registry.TryAcquire(@"C:\repos\repo-a", firstRunId, out _);

        var acquired = registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), out var conflictingRunId);

        Assert.False(acquired);
        Assert.Equal(firstRunId, conflictingRunId);
    }

    [Fact]
    public void Release_frees_the_repo_for_a_subsequent_acquire()
    {
        var registry = new RunRegistry();
        registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), out _);

        registry.Release(@"C:\repos\repo-a");
        var acquired = registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), out _);

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
            return registry.TryAcquire(@"C:\repos\repo-a", Guid.NewGuid(), out var conflictingRunId);
        })));

        Assert.Equal(1, results.Count(acquired => acquired));
    }
}
