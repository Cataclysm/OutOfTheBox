// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// Background sampler running <c>git fetch</c> against every repository under the configured root on
/// its own, much slower cadence (default 300s per <see cref="ServiceOptions.RepositoryFetchIntervalSeconds"/>)
/// than <see cref="RepositoryStatsSampler"/>'s git-status/size cadences - those are purely local
/// (already-known refs), so ahead/behind and "remote gone" only ever reflect what the last real
/// fetch/pull/push saw. Without this, that only ever happened when an operator manually triggered
/// pull/push/fetch (or an MCP `git_run`) against a repository - a repository nobody touches would
/// show stale ahead/behind indefinitely. Goes through <see cref="IRepositoryManager.FetchAsync"/>,
/// not a raw <c>git fetch</c> invocation, so every existing side effect of a fetch (the per-repository
/// lock, credential-outcome recording feeding <see cref="RepositoryCredentialHealth"/>, the immediate
/// stats refresh) applies here identically to an operator-triggered one - a side benefit is that a
/// repository nobody has manually touched now also self-detects (and self-clears) a broken credential
/// via this same background sweep, not just the git-status/ahead-behind data itself.
/// </summary>
public sealed class RepositoryFetchSampler(
    IOptions<ServiceOptions> options,
    IServiceScopeFactory serviceScopeFactory,
    IHostApplicationLifetime applicationLifetime,
    CredentialSyncService credentialSyncService,
    ILogger<RepositoryFetchSampler> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.RepositoryFetchIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            // Deliberately waits for the host to be fully up (Kestrel accepting connections, every
            // other hosted service's own StartAsync already returned) before ever running - unlike
            // RepositoryStatsSampler's "compute once immediately" first call, a fetch is a real
            // network operation per repository, and startup (EF Core migration, certificate loading,
            // every other hosted service's own StartAsync potentially still in flight) is exactly
            // when the process is least ready to absorb that. Once past this point, the first sweep
            // still runs immediately rather than waiting a full RepositoryFetchIntervalSeconds (5
            // minutes by default) for its first one just because the service happened to just start.
            await WaitForApplicationStartedAsync(stoppingToken);

            // Actively starts (not merely awaits) CredentialSyncService's first sync sweep - a
            // repository whose git host credential the OS-level store had lost would otherwise race
            // this fetch and fail with a stale/missing credential on every restart, self-correcting
            // only on the *next* fetch cadence instead of the very first one. EnsureInitialSyncAsync
            // runs the sweep itself if CredentialSyncService's own loop hasn't gotten there yet,
            // rather than waiting on a signal that loop sets on its own schedule - see that method's
            // own remarks for why the distinction matters.
            await credentialSyncService.EnsureInitialSyncAsync(stoppingToken);

            await FetchAllOnceAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FetchAllOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown (stoppingToken cancelled) - not an error.
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken stoppingToken)
    {
        var startedTcs = new TaskCompletionSource();
        await using var registration = applicationLifetime.ApplicationStarted.Register(() => startedTcs.TrySetResult());
        await startedTcs.Task.WaitAsync(stoppingToken);
    }

    /// <summary>
    /// Runs one fetch sweep over every git repository under the configured root - exposed publicly so
    /// a test can exercise it directly, the same way <see cref="RepositoryStatsSampler.RecomputeAllOnceAsync"/>
    /// is, without needing the real <see cref="PeriodicTimer"/>-driven loop.
    /// </summary>
    public async Task FetchAllOnceAsync(CancellationToken cancellationToken)
    {
        // A fresh scope per sweep, not a captive constructor dependency - IRepositoryManager is
        // scoped (it depends on the scoped IRunRepository), and this is a singleton-lifetime
        // BackgroundService, the same "resolve a fresh scope internally" reasoning GitCredentialStore's
        // own remarks already document for the same underlying constraint.
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var repositoryManager = scope.ServiceProvider.GetRequiredService<IRepositoryManager>();

        IReadOnlyList<RepositorySummary> summaries;
        try
        {
            summaries = await repositoryManager.ListAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enumerate repositories for the background fetch sweep.");
            return;
        }

        foreach (var summary in summaries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Stats not computed yet (a repository RepositoryStatsSampler hasn't reached its first
            // tick for since startup) defaults IsGitRepository to false - harmless, since that sampler
            // runs on a much faster cadence and will have caught up well before this one's next tick.
            if (!summary.IsGitRepository)
            {
                continue;
            }

            await FetchOneAsync(repositoryManager, summary.Name, cancellationToken);
        }
    }

    private async Task FetchOneAsync(IRepositoryManager repositoryManager, string name, CancellationToken cancellationToken)
    {
        try
        {
            var result = await repositoryManager.FetchAsync(name, cancellationToken);

            // Succeeded needs no action (FetchAsync already refreshed and published this
            // repository's stats). Rejected (busy with another run, deleted since being listed) is
            // an entirely routine outcome for an unattended sweep - it just tries again next tick,
            // not worth logging. Only a real git failure gets a log line, so an operator can tell
            // "this repository's fetch keeps failing" apart from silence.
            if (result is RepositoryGitActionResult.Failed failed)
            {
                logger.LogWarning("Background fetch failed for repository '{Name}': {ErrorMessage}", name, failed.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // Never let one repository's unexpected failure stop the sweep, let alone the whole
            // BackgroundService - the same crash-resilience reasoning RepositoryStatsSampler.GuardAsync
            // documents for the identical class of risk.
            logger.LogWarning(ex, "Unexpected error running the background fetch for repository '{Name}'.", name);
        }
    }
}
