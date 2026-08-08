// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Diagnostics;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Infrastructure.Monitoring;

namespace OutOfTheBox.UnitTests.Infrastructure.Monitoring;

/// <summary>
/// Covers <see cref="ProcessMonitor"/>'s rejection paths only - no real kill is ever exercised
/// here (that needs a real disposable child process, which belongs in BehaviorTests per this
/// project's "no real process spawning in UnitTests" convention; see
/// <c>HostResourceMonitoring.feature</c>'s "killing a listed process" scenario). Both tests here
/// use real <see cref="RunRegistry"/>/WMI calls but are constructed so the code path never reaches
/// an actual <see cref="Process.Kill()"/> call.
/// </summary>
public sealed class ProcessMonitorTests
{
    [Fact]
    public async Task KillAsync_rejects_a_pid_when_nothing_is_tracked()
    {
        var monitor = new ProcessMonitor(new RunRegistry());

        // No tracked roots at all - must reject before ever touching WMI/Process.GetProcessById,
        // regardless of what pid/StartTime is supplied.
        var killed = await monitor.KillAsync(Environment.ProcessId, DateTime.UtcNow, CancellationToken.None);

        Assert.False(killed);
    }

    [Fact]
    public async Task KillAsync_rejects_a_pid_not_under_any_tracked_roots_tree()
    {
        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();

        // A process id essentially guaranteed not to correspond to any real running process, so
        // its WMI-discovered "tree" is empty - the current test process's own pid is then
        // definitely not a member of it, and KillAsync must reject before ever calling
        // Process.GetProcessById/Kill on the real, running test process.
        const int fakeRootProcessId = 999_999;
        var runId = Guid.NewGuid();
        registry.TryAcquire(@"C:\repositories\example", runId, cts, out _);
        registry.SetProcessId(runId, fakeRootProcessId);

        var monitor = new ProcessMonitor(registry);

        var killed = await monitor.KillAsync(Environment.ProcessId, DateTime.UtcNow, CancellationToken.None);

        Assert.False(killed);
    }
}
