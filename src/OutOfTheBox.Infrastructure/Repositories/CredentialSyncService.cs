// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// Background service that periodically re-derives every OS-level credential store (git's credential
/// helper; this machine's NuGet configuration for a generic feed) from this service's own database,
/// which is now the durable source of truth for every credential (see <see cref="ICredentialProtector"/>'s
/// own remarks) - repairing whatever the OS-level store lost (a plain uninstall-then-reinstall, which
/// recreates the dedicated service account with a new SID and an empty vault; manual tampering) without
/// requiring the operator to re-enter any PAT. Azure DevOps Artifacts feeds are skipped entirely - the
/// database is already their sole source (<see cref="INuGetFeedCredentialStore.GetAzureDevOpsArtifactsEndpointCredentialsAsync"/>
/// reads it directly on every <c>dotnet_run</c> spawn), so there is nothing external to sync for them.
/// Same <see cref="PeriodicTimer"/>/fresh-scope-per-tick/per-item-try-catch/public-single-sweep-method
/// shape as <see cref="RepositoryFetchSampler"/>/<c>RepositoryStatsSampler</c> - an unhandled exception
/// escaping <see cref="ExecuteAsync"/> would otherwise stop the whole host (the default
/// <c>BackgroundServiceExceptionBehavior</c>).
/// </summary>
public sealed class CredentialSyncService(
    IOptions<ServiceOptions> options,
    IServiceScopeFactory serviceScopeFactory,
    ICredentialProtector credentialProtector,
    ICredentialEventBus credentialEventBus,
    IProcessRunner processRunner,
    IHostApplicationLifetime applicationLifetime,
    ILogger<CredentialSyncService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.CredentialSyncIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            // Same "wait for the host to be fully up before touching anything external" reasoning as
            // RepositoryFetchSampler - startup is exactly when the process is least ready to spawn
            // git.exe or touch the machine's NuGet configuration.
            await WaitForApplicationStartedAsync(stoppingToken);
            await SyncAllOnceAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SyncAllOnceAsync(stoppingToken);
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
    /// Runs one full sync sweep over every stored git host and NuGet feed credential - exposed
    /// publicly so a test can exercise it directly, without needing the real <see cref="PeriodicTimer"/>-
    /// driven loop, the same way <see cref="RepositoryFetchSampler.FetchAllOnceAsync"/> is.
    /// </summary>
    public async Task SyncAllOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var repairedAny = await SyncGitHostsAsync(dbContext, cancellationToken);
        repairedAny |= await SyncGenericNuGetFeedsAsync(dbContext, cancellationToken);

        if (repairedAny)
        {
            credentialEventBus.Publish();
        }
    }

    private async Task<bool> SyncGitHostsAsync(OutOfTheBoxDbContext dbContext, CancellationToken cancellationToken)
    {
        var authorizations = await dbContext.GitHostAuthorizations.AsNoTracking()
            .Where(a => a.EncryptedToken != null)
            .ToListAsync(cancellationToken);

        var repairedAny = false;
        foreach (var authorization in authorizations)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            repairedAny |= await SyncGitHostAsync(authorization, cancellationToken);
        }

        return repairedAny;
    }

    private async Task<bool> SyncGitHostAsync(GitHostAuthorization authorization, CancellationToken cancellationToken)
    {
        if (!credentialProtector.TryDecrypt(authorization.EncryptedToken!, out var token))
        {
            // Logged by DpapiCredentialProtector itself - this row needs re-authorization, nothing
            // this sweep can repair for it.
            return false;
        }

        try
        {
            var current = await GitCredentialWriter.FillPasswordAsync(processRunner, logger, options.Value.RootDirectory, authorization.Host, cancellationToken);
            if (current == token)
            {
                return false;
            }

            var repaired = await GitCredentialWriter.ApproveAndVerifyAsync(processRunner, logger, options.Value.RootDirectory, authorization.Host, token, cancellationToken);
            if (!repaired)
            {
                logger.LogWarning("Could not repair git credential helper's entry for host '{Host}' - the write did not verify.", authorization.Host);
            }

            return repaired;
        }
        catch (Exception ex)
        {
            // Never let one host's unexpected failure (git.exe unreachable, a timeout) stop the
            // sweep, let alone the whole BackgroundService - the same crash-resilience reasoning
            // RepositoryStatsSampler.GuardAsync documents for the identical class of risk.
            logger.LogWarning(ex, "Unexpected error syncing the git credential for host '{Host}'.", authorization.Host);
            return false;
        }
    }

    private async Task<bool> SyncGenericNuGetFeedsAsync(OutOfTheBoxDbContext dbContext, CancellationToken cancellationToken)
    {
        var authorizations = await dbContext.NuGetFeedAuthorizations.AsNoTracking()
            .Where(a => a.EncryptedPassword != null)
            .ToListAsync(cancellationToken);

        var repairedAny = false;
        foreach (var authorization in authorizations)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            repairedAny |= SyncGenericNuGetFeed(authorization);
        }

        return repairedAny;
    }

    private bool SyncGenericNuGetFeed(NuGetFeedAuthorization authorization)
    {
        if (!Uri.TryCreate(authorization.FeedUrl, UriKind.Absolute, out var uri) || AzureArtifactsFeedClassifier.IsAzureDevOpsArtifactsFeed(uri))
        {
            // Azure DevOps Artifacts feeds have no external store to sync - the DB is already their
            // sole source, read directly by GetAzureDevOpsArtifactsEndpointCredentialsAsync.
            return false;
        }

        if (!credentialProtector.TryDecrypt(authorization.EncryptedPassword!, out var token))
        {
            return false;
        }

        try
        {
            if (NuGetFeedConfigWriter.ReadCurrentPassword(authorization.FeedUrl) == token)
            {
                return false;
            }

            var failure = NuGetFeedConfigWriter.WriteAndVerify(authorization.FeedUrl, token);
            if (failure is null)
            {
                return true;
            }

            logger.LogWarning("Could not repair this machine's NuGet configuration entry for feed '{FeedUrl}' - the write did not verify.", authorization.FeedUrl);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error syncing the NuGet credential for feed '{FeedUrl}'.", authorization.FeedUrl);
            return false;
        }
    }
}
