// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Configuration;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Infrastructure.Persistence;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="INuGetFeedCredentialStore" />
/// <remarks>
/// Routes each feed URL to one of two mechanisms via <see cref="AzureArtifactsFeedClassifier"/> - see
/// design.md's "storage is a dual mechanism" decision. A non-Azure-DevOps feed's credential is
/// written into this machine's default NuGet configuration via <see cref="NuGetFeedConfigWriter"/>;
/// an Azure DevOps Artifacts feed's credential has no external durable store of its own (the Azure
/// Artifacts Credential Provider's own mechanism is just an environment variable), so the DB is its
/// sole store. Every feed's credential is additionally persisted here too
/// (<see cref="NuGetFeedAuthorization.EncryptedPassword"/>, machine-scoped-DPAPI-encrypted via
/// <see cref="ICredentialProtector"/>) - for a generic feed this is a second, independently-durable
/// copy alongside the NuGet-config one, kept in sync by <c>CredentialSyncService</c>. Registered
/// singleton - resolves its own scoped <see cref="OutOfTheBoxDbContext"/> per call via
/// <see cref="IServiceScopeFactory"/>, the same pattern <c>GitCredentialStore</c> uses.
/// </remarks>
public sealed class NuGetFeedCredentialStore(IServiceScopeFactory serviceScopeFactory, ICredentialEventBus credentialEventBus, ICredentialProtector credentialProtector) : INuGetFeedCredentialStore
{
    /// <inheritdoc />
    public async Task<NuGetCredentialAuthorizeResult> AuthorizeAsync(string feedUrl, string token, CancellationToken cancellationToken)
    {
        if (!TryNormalize(feedUrl, out var normalizedUrl, out var uri))
        {
            return new NuGetCredentialAuthorizeResult.InvalidFeedUrl();
        }

        if (AzureArtifactsFeedClassifier.IsAzureDevOpsArtifactsFeed(uri))
        {
            if (!IsCredentialProviderInstalled())
            {
                return new NuGetCredentialAuthorizeResult.CredentialProviderNotInstalled();
            }
        }
        else
        {
            var genericFailure = NuGetFeedConfigWriter.WriteAndVerify(normalizedUrl, token);
            if (genericFailure is not null)
            {
                return genericFailure;
            }
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();
        await UpsertAuthorizationAsync(dbContext, normalizedUrl, credentialProtector.Encrypt(token), cancellationToken);

        credentialEventBus.Publish();
        return new NuGetCredentialAuthorizeResult.Succeeded();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetFeedAuthorizationSummary>> ListAuthorizedFeedsAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var authorizations = await dbContext.NuGetFeedAuthorizations.AsNoTracking().ToListAsync(cancellationToken);

        return [.. authorizations
            .OrderBy(a => a.FeedUrl, StringComparer.Ordinal)
            .Select(a => new NuGetFeedAuthorizationSummary(a.FeedUrl, a.AuthorizedAtUtc))];
    }

    /// <inheritdoc />
    public async Task<NuGetCredentialRevokeResult> RevokeAsync(string feedUrl, CancellationToken cancellationToken)
    {
        if (!TryNormalize(feedUrl, out var normalizedUrl, out var uri))
        {
            return new NuGetCredentialRevokeResult.NothingToRevoke();
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var existing = await dbContext.NuGetFeedAuthorizations.FirstOrDefaultAsync(a => a.FeedUrl == normalizedUrl, cancellationToken);
        if (existing is null)
        {
            return new NuGetCredentialRevokeResult.NothingToRevoke();
        }

        if (!AzureArtifactsFeedClassifier.IsAzureDevOpsArtifactsFeed(uri))
        {
            try
            {
                var settings = Settings.LoadDefaultSettings(root: null);
                new PackageSourceProvider(settings).RemovePackageSource(normalizedUrl);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new NuGetCredentialRevokeResult.ConfigurationUnwritable(ex.Message);
            }
        }

        dbContext.NuGetFeedAuthorizations.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);

        credentialEventBus.Publish();
        return new NuGetCredentialRevokeResult.Revoked();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetFeedEndpointCredential>> GetAzureDevOpsArtifactsEndpointCredentialsAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var authorizations = await dbContext.NuGetFeedAuthorizations.AsNoTracking()
            .Where(a => a.EncryptedPassword != null)
            .ToListAsync(cancellationToken);

        var credentials = new List<NuGetFeedEndpointCredential>();
        foreach (var authorization in authorizations)
        {
            if (credentialProtector.TryDecrypt(authorization.EncryptedPassword!, out var password))
            {
                credentials.Add(new NuGetFeedEndpointCredential(authorization.FeedUrl, password));
            }

            // A decrypt failure here means the credential was encrypted under a since-migrated-away
            // key/scope and needs re-authorization (see ICredentialProtector's own remarks) - skipped,
            // not thrown, so one undecryptable feed never breaks every other feed's restore/dotnet_run.
        }

        return credentials;
    }

    // Checks for the exact file NUGET_NETCORE_PLUGIN_PATHS will be pointed at (see
    // NuGetCredentialProviderLocation.PluginFilePath's own remarks) - not just "some exe exists in
    // this directory," so a partially-extracted or wrong-shaped bundle fails this check the same way
    // a missing one does, rather than reporting installed and failing confusingly later at restore time.
    private static bool IsCredentialProviderInstalled() => File.Exists(NuGetCredentialProviderLocation.PluginFilePath);

    private static async Task UpsertAuthorizationAsync(OutOfTheBoxDbContext dbContext, string normalizedUrl, byte[]? encryptedPassword, CancellationToken cancellationToken)
    {
        var existing = await dbContext.NuGetFeedAuthorizations.FirstOrDefaultAsync(a => a.FeedUrl == normalizedUrl, cancellationToken);
        EfUpsert.Save(dbContext, existing, new NuGetFeedAuthorization(normalizedUrl, DateTimeOffset.UtcNow, encryptedPassword));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Feed URLs are matched by their canonical Uri.AbsoluteUri form (lowercase scheme+host,
    // percent-encoding normalized) rather than the caller's raw string, so the same feed can't end up
    // authorized twice under two differently-cased/formatted spellings of the same URL.
    private static bool TryNormalize(string? feedUrl, out string normalizedUrl, out Uri uri)
    {
        normalizedUrl = string.Empty;
        uri = null!;

        if (string.IsNullOrWhiteSpace(feedUrl)
            || !Uri.TryCreate(feedUrl.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        uri = parsed;
        normalizedUrl = parsed.AbsoluteUri;
        return true;
    }
}
