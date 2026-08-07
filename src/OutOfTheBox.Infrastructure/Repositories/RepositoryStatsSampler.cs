// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// Background sampler recomputing every repository's size/git status on a slow cadence (default
/// 60s, per <see cref="ServiceOptions.RepositoryStatsSamplerIntervalSeconds"/>) plus immediately
/// whenever a run against a specific repo reaches a terminal state - per design.md's "Repository
/// stats" decision. Distinct from, and runs at a different cadence than, the host/process resource
/// sampler (Section 14).
/// </summary>
public sealed class RepositoryStatsSampler(
    IOptions<ServiceOptions> options,
    IRepositoryStatsProvider statsProvider,
    RepositoryStatsCache statsCache,
    IRunEventBus runEventBus) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var subscription = runEventBus.Subscribe(OnRunEvent);

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.RepositoryStatsSamplerIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        // Compute once immediately at startup, so the dashboard isn't stuck showing "computing…"
        // for a full interval right after the service starts.
        await RecomputeAllAsync(stoppingToken);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RecomputeAllAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown (stoppingToken cancelled) - not an error.
        }
    }

    private void OnRunEvent(RunEvent runEvent)
    {
        if (runEvent.Type != RunEventType.Terminal)
        {
            return;
        }

        // Recompute just this one repo, not the whole set - the event-driven half of the "slow
        // cadence plus event-driven recompute" decision.
        _ = RecomputeOneAsync(runEvent.RepoPath, CancellationToken.None);
    }

    private async Task RecomputeAllAsync(CancellationToken cancellationToken)
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

            await RecomputeOneAsync(directory, cancellationToken);
        }
    }

    private async Task RecomputeOneAsync(string repoPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(repoPath))
        {
            // Deleted between being enumerated/referenced and this tick running - nothing to
            // compute; RepositoryManager.DeleteAsync already removes its cache entry directly.
            return;
        }

        try
        {
            var stats = await statsProvider.ComputeAsync(repoPath, cancellationToken);
            statsCache.Set(Path.GetFileName(repoPath), stats);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
