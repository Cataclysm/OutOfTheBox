// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// Background sampler recomputing every repository's stats on two independent cadences - git status
/// (fast, default 10s per <see cref="ServiceOptions.RepositoryGitStatusIntervalSeconds"/>: a handful
/// of short-lived <c>git</c> invocations) and total size (slow, default 60s per
/// <see cref="ServiceOptions.RepositoryStatsSamplerIntervalSeconds"/>: a full recursive directory
/// walk) - plus a full (both-halves) recompute immediately whenever a run against a specific
/// repository reaches a terminal state, per design.md's "Repository stats" decision. Distinct from,
/// and runs at different cadences than, the host/process resource sampler (Section 14).
/// </summary>
public sealed class RepositoryStatsSampler(
    IOptions<ServiceOptions> options,
    IRepositoryStatsProvider statsProvider,
    RepositoryStatsCache statsCache,
    IRunEventBus runEventBus,
    IRepositoryStatsEventBus statsEventBus) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var subscription = runEventBus.Subscribe(OnRunEvent);

        // Compute both halves once immediately at startup, so the dashboard isn't stuck showing
        // "computing…" until the first tick of whichever cadence is slower.
        await ForEachRepositoryAsync(RecomputeOneFullAsync, stoppingToken);

        try
        {
            await Task.WhenAll(
                GitStatusLoopAsync(stoppingToken),
                SizeLoopAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown (stoppingToken cancelled) - not an error.
        }
    }

    private async Task GitStatusLoopAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.RepositoryGitStatusIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ForEachRepositoryAsync(RecomputeOneGitStatusAsync, stoppingToken);
        }
    }

    private async Task SizeLoopAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.RepositoryStatsSamplerIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ForEachRepositoryAsync(RecomputeOneSizeAsync, stoppingToken);
        }
    }

    private void OnRunEvent(RunEvent runEvent)
    {
        if (runEvent.Type != RunEventType.Terminal)
        {
            return;
        }

        // Recompute just this one repository, not the whole set - the event-driven half of the "two
        // cadences plus event-driven recompute" decision. A full (both-halves) recompute, not just
        // git status: a run completing (e.g. a build producing bin/obj output) can plausibly change
        // on-disk size too, not only git state.
        _ = RecomputeOneFullAsync(runEvent.RepositoryPath, CancellationToken.None);
    }

    private async Task ForEachRepositoryAsync(Func<string, CancellationToken, Task> recompute, CancellationToken cancellationToken)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await recompute(directory, cancellationToken);
        }
    }

    private Task RecomputeOneFullAsync(string repositoryPath, CancellationToken cancellationToken) =>
        GuardAsync(repositoryPath, async name =>
        {
            var stats = await statsProvider.ComputeAsync(repositoryPath, cancellationToken);
            statsCache.Set(name, stats);
        });

    private Task RecomputeOneGitStatusAsync(string repositoryPath, CancellationToken cancellationToken) =>
        GuardAsync(repositoryPath, async name =>
        {
            var gitStatus = await statsProvider.ComputeGitStatusAsync(repositoryPath, cancellationToken);
            statsCache.SetGitStatus(name, gitStatus);
        });

    private Task RecomputeOneSizeAsync(string repositoryPath, CancellationToken cancellationToken) =>
        GuardAsync(repositoryPath, async name =>
        {
            var size = await statsProvider.ComputeSizeAsync(repositoryPath, cancellationToken);
            statsCache.SetSize(name, size);
        });

    /// <summary>
    /// Runs <paramref name="recompute"/> for the repository at <paramref name="repositoryPath"/>,
    /// then publishes its stats-changed event - and, critically, ensures neither a missing directory
    /// nor any exception from <paramref name="recompute"/> can ever propagate out of this method.
    /// This is a <see cref="BackgroundService"/>; an exception escaping <see cref="ExecuteAsync"/>
    /// stops the entire host by default (<c>BackgroundServiceExceptionBehavior.StopHost</c>), which
    /// previously happened here (a <c>Win32Exception</c> from an unreachable <c>git.exe</c>) and
    /// silently ended stats updates for every repository, not just the one that failed.
    /// </summary>
    private async Task GuardAsync(string repositoryPath, Func<string, Task> recompute)
    {
        if (!Directory.Exists(repositoryPath))
        {
            // Deleted between being enumerated/referenced and this tick running - nothing to
            // compute; RepositoryManager.DeleteAsync already removes its cache entry directly.
            return;
        }

        var name = Path.GetFileName(repositoryPath);

        try
        {
            await recompute(name);

            // Otherwise a dashboard page open on Repositories/RepositoryDetail while this tick runs
            // has no way to learn the cache changed - it isn't itself a RunEvent, so without this,
            // the page would sit showing whatever it last snapshotted until some unrelated event
            // triggered another refresh. See IRepositoryStatsEventBus's own remarks.
            statsEventBus.Publish(name);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Skip this repository for this tick; the next tick (of whichever cadence), or the next
            // event-driven recompute, tries again.
        }
    }
}
