// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IGitCredentialStore" />
/// <remarks>
/// Shells out to git's own <c>credential approve</c>/<c>fill</c>/<c>reject</c> protocol rather than
/// talking to Windows Credential Manager directly - see design.md's "storage" decision. Every
/// invocation runs from the configured root directory (these commands are host-scoped, not
/// repository-scoped, so any writable, already-existing directory works). Registered singleton -
/// <see cref="GitRepositoryStatsProvider"/> (itself singleton, consumed by the singleton-lifetime
/// <c>RepositoryStatsSampler</c> background service) depends on this port too, so it resolves its
/// own scoped <see cref="OutOfTheBoxDbContext"/> per call via <see cref="IServiceScopeFactory"/>
/// rather than taking one as a captive constructor dependency - the same pattern
/// <c>RepositoryManager.RunCloneToCompletionAsync</c> already uses for its own fire-and-forget,
/// potentially-outlives-the-caller's-scope database access.
/// </remarks>
public sealed class GitCredentialStore(
    IProcessRunner processRunner,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<ServiceOptions> options,
    ICredentialEventBus credentialEventBus,
    ICredentialProtector credentialProtector,
    ILogger<GitCredentialStore> logger) : IGitCredentialStore
{
    /// <inheritdoc />
    public async Task<GitCredentialAuthorizeResult> AuthorizeAsync(string host, string token, CancellationToken cancellationToken)
    {
        var normalizedHost = Normalize(host);

        string? credentialHelper;
        try
        {
            credentialHelper = (await GitCaptureRunner.CaptureAsync(processRunner, logger, options.Value.RootDirectory, ["config", "--get", "credential.helper"], cancellationToken))?.Trim();
        }
        catch (Win32Exception ex)
        {
            return new GitCredentialAuthorizeResult.GitUnreachable(ex.Message);
        }

        if (string.IsNullOrEmpty(credentialHelper))
        {
            return new GitCredentialAuthorizeResult.NoCredentialHelperConfigured();
        }

        bool verified;
        try
        {
            verified = await GitCredentialWriter.ApproveAndVerifyAsync(processRunner, logger, options.Value.RootDirectory, normalizedHost, token, cancellationToken);
        }
        catch (Win32Exception ex)
        {
            return new GitCredentialAuthorizeResult.GitUnreachable(ex.Message);
        }

        if (!verified)
        {
            return new GitCredentialAuthorizeResult.VerificationFailed();
        }

        await using (var scope = serviceScopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();
            await UpsertAuthorizationAsync(dbContext, normalizedHost, credentialProtector.Encrypt(token), cancellationToken);
        }

        credentialEventBus.Publish();
        return new GitCredentialAuthorizeResult.Succeeded();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHostAuthorizationSummary>> ListAuthorizedHostsAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var authorizations = await dbContext.GitHostAuthorizations.AsNoTracking().ToListAsync(cancellationToken);
        var health = await dbContext.GitHostCredentialHealth.AsNoTracking().ToDictionaryAsync(h => h.Host, cancellationToken);

        return [.. authorizations
            .OrderBy(a => a.Host, StringComparer.Ordinal)
            .Select(a => new GitHostAuthorizationSummary(a.Host, a.AuthorizedAtUtc, GitHostCredentialHealth.NeedsCredential(health.GetValueOrDefault(a.Host))))];
    }

    /// <inheritdoc />
    public async Task<GitCredentialRevokeResult> RevokeAsync(string host, CancellationToken cancellationToken)
    {
        var normalizedHost = Normalize(host);

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var existing = await dbContext.GitHostAuthorizations.FirstOrDefaultAsync(a => a.Host == normalizedHost, cancellationToken);
        if (existing is null)
        {
            return new GitCredentialRevokeResult.NothingToRevoke();
        }

        try
        {
            await GitCredentialWriter.RejectAsync(processRunner, logger, options.Value.RootDirectory, normalizedHost, cancellationToken);
        }
        catch (Win32Exception ex)
        {
            return new GitCredentialRevokeResult.GitUnreachable(ex.Message);
        }

        dbContext.GitHostAuthorizations.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Deliberately does NOT proactively mark this host as needing credential (an earlier version
        // of this method did, via RecordOutcomeAsync(normalizedHost, false, ...), and it was wrong -
        // GitHostCredentialHealth is host-scoped, not per-repository, so a host can easily have both
        // private and public repositories cloned from it (a real, reported scenario: one private
        // GitHub repo plus several public ones). Marking the whole host as failing here flags every
        // public repository too, even though nothing about them actually changed - they never needed
        // a credential before this revoke and still don't. NeedsCredential instead stays purely
        // reactive (only ever set by a real pull/push/fetch/clone failure), the same way it already
        // handles every other way a credential can stop working (expiry, revocation on the remote
        // host itself) - a deliberate, already-accepted staleness trade-off (see design.md), not
        // something a same-host revoke should special-case differently. Still refreshes affected
        // repositories' cached stats immediately - this never changes what NeedsCredential evaluates
        // to, only how quickly the dashboard reflects health state that was already true beforehand.
        await RefreshAffectedRepositoriesAsync(normalizedHost, cancellationToken);

        credentialEventBus.Publish();
        return new GitCredentialRevokeResult.Revoked();
    }

    /// <summary>
    /// Immediately recomputes and publishes the git-status half (which carries <c>NeedsCredential</c>)
    /// for every repository whose <c>origin</c> remote resolves to <paramref name="normalizedHost"/>,
    /// after a credential change for that host - so the dashboard/list_repositories reflect it
    /// without waiting for <c>RepositoryStatsSampler</c>'s own next periodic tick. Resolves
    /// <see cref="IRepositoryManager"/>/<see cref="IRepositoryStatsProvider"/>/<see cref="RepositoryStatsCache"/>/
    /// <see cref="IRepositoryStatsEventBus"/> lazily from a fresh scope rather than as constructor
    /// dependencies - <see cref="OutOfTheBox.Infrastructure.Repositories.GitRepositoryStatsProvider"/>
    /// (the concrete <see cref="IRepositoryStatsProvider"/>) itself depends on this class, so taking
    /// any of these as a captive constructor dependency here would be a circular singleton graph;
    /// resolving them only when this method actually runs sidesteps that entirely, the same reasoning
    /// this class already applies to <see cref="OutOfTheBoxDbContext"/>. Best-effort: a repository
    /// this can't enumerate or recompute for is logged and skipped, never fails the revoke itself.
    /// </summary>
    private async Task RefreshAffectedRepositoriesAsync(string normalizedHost, CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var repositoryManager = scope.ServiceProvider.GetRequiredService<IRepositoryManager>();
        var statsProvider = scope.ServiceProvider.GetRequiredService<IRepositoryStatsProvider>();
        var statsCache = scope.ServiceProvider.GetRequiredService<RepositoryStatsCache>();
        var statsEventBus = scope.ServiceProvider.GetRequiredService<IRepositoryStatsEventBus>();

        IReadOnlyList<RepositorySummary> summaries;
        try
        {
            summaries = await repositoryManager.ListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enumerate repositories to refresh after revoking the credential for '{Host}'.", normalizedHost);
            return;
        }

        foreach (var summary in summaries)
        {
            var origin = summary.Remotes.FirstOrDefault(r => r.Name == "origin");
            if (origin is null || !GitRemoteUrlParser.TryGetHost(origin.Url, out var repoHost) || Normalize(repoHost) != normalizedHost)
            {
                continue;
            }

            try
            {
                var gitStatus = await statsProvider.ComputeGitStatusAsync(summary.Path, cancellationToken);
                statsCache.SetGitStatus(summary.Name, gitStatus);
                statsEventBus.Publish(summary.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not refresh repository '{Repository}' after revoking the credential for '{Host}'.", summary.Name, normalizedHost);
            }
        }
    }

    /// <inheritdoc />
    public async Task RecordOutcomeAsync(string host, bool succeeded, CancellationToken cancellationToken)
    {
        var normalizedHost = Normalize(host);
        var now = DateTimeOffset.UtcNow;

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var existing = await dbContext.GitHostCredentialHealth.FirstOrDefaultAsync(h => h.Host == normalizedHost, cancellationToken);
        var updated = existing is null
            ? new GitHostCredentialHealth(normalizedHost, succeeded ? null : now, succeeded ? now : null)
            : existing with
            {
                LastAuthFailureAtUtc = succeeded ? existing.LastAuthFailureAtUtc : now,
                LastAuthSuccessAtUtc = succeeded ? now : existing.LastAuthSuccessAtUtc,
            };

        EfUpsert.Save(dbContext, existing, updated);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GitHostCredentialHealth?> GetHealthAsync(string host, CancellationToken cancellationToken)
    {
        var normalizedHost = Normalize(host);

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        return await dbContext.GitHostCredentialHealth.AsNoTracking().FirstOrDefaultAsync(h => h.Host == normalizedHost, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordRepositoryOutcomeAsync(string repositoryPath, bool succeeded, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var existing = await dbContext.RepositoryCredentialHealth.FirstOrDefaultAsync(h => h.RepositoryPath == repositoryPath, cancellationToken);
        var updated = existing is null
            ? new RepositoryCredentialHealth(repositoryPath, succeeded ? null : now, succeeded ? now : null)
            : existing with
            {
                LastAuthFailureAtUtc = succeeded ? existing.LastAuthFailureAtUtc : now,
                LastAuthSuccessAtUtc = succeeded ? now : existing.LastAuthSuccessAtUtc,
            };

        EfUpsert.Save(dbContext, existing, updated);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RepositoryCredentialHealth?> GetRepositoryHealthAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        return await dbContext.RepositoryCredentialHealth.AsNoTracking().FirstOrDefaultAsync(h => h.RepositoryPath == repositoryPath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RenameRepositoryHealthAsync(string oldRepositoryPath, string newRepositoryPath, CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        await dbContext.RepositoryCredentialHealth
            .Where(h => h.RepositoryPath == oldRepositoryPath)
            .ExecuteUpdateAsync(setters => setters.SetProperty(h => h.RepositoryPath, newRepositoryPath), cancellationToken);
    }

    private static async Task UpsertAuthorizationAsync(OutOfTheBoxDbContext dbContext, string normalizedHost, byte[] encryptedToken, CancellationToken cancellationToken)
    {
        var existing = await dbContext.GitHostAuthorizations.FirstOrDefaultAsync(a => a.Host == normalizedHost, cancellationToken);
        EfUpsert.Save(dbContext, existing, new GitHostAuthorization(normalizedHost, DateTimeOffset.UtcNow, encryptedToken));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Hosts are compared/stored lower-invariant rather than relying on a provider-specific
    // collation for case-insensitive matching (see OutOfTheBoxDbContext's own remark).
    private static string Normalize(string host) => host.Trim().ToLowerInvariant();
}
