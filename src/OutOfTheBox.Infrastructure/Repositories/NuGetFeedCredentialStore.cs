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
/// written into this machine's default NuGet configuration via <c>NuGet.Configuration</c>, never
/// touching this service's own database; an Azure DevOps Artifacts feed's credential is DPAPI-
/// encrypted (<see cref="NuGetCredentialProtector"/>) and persisted here instead, since the Azure
/// Artifacts Credential Provider's own mechanism (an environment variable) has no durability of its
/// own. Registered singleton - resolves its own scoped <see cref="OutOfTheBoxDbContext"/> per call via
/// <see cref="IServiceScopeFactory"/>, the same pattern <c>GitCredentialStore</c> uses.
/// </remarks>
public sealed class NuGetFeedCredentialStore(IServiceScopeFactory serviceScopeFactory, ICredentialEventBus credentialEventBus) : INuGetFeedCredentialStore
{
    // Any non-empty username works for both GitHub Packages and Azure Artifacts when the password is
    // a valid PAT (see design.md's "no username parameter" decision - unverified, folded into live
    // verification). Fixed and never caller-supplied, so the feed URL alone is always the match key.
    private const string PlaceholderUsername = "nuget";

    /// <inheritdoc />
    public async Task<NuGetCredentialAuthorizeResult> AuthorizeAsync(string feedUrl, string token, CancellationToken cancellationToken)
    {
        if (!TryNormalize(feedUrl, out var normalizedUrl, out var uri))
        {
            return new NuGetCredentialAuthorizeResult.InvalidFeedUrl();
        }

        byte[]? encryptedPassword = null;

        if (AzureArtifactsFeedClassifier.IsAzureDevOpsArtifactsFeed(uri))
        {
            if (!IsCredentialProviderInstalled())
            {
                return new NuGetCredentialAuthorizeResult.CredentialProviderNotInstalled();
            }

            encryptedPassword = NuGetCredentialProtector.Encrypt(token);
            if (NuGetCredentialProtector.Decrypt(encryptedPassword) != token)
            {
                return new NuGetCredentialAuthorizeResult.VerificationFailed();
            }
        }
        else
        {
            var genericFailure = AuthorizeGenericFeed(normalizedUrl, token);
            if (genericFailure is not null)
            {
                return genericFailure;
            }
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();
        await UpsertAuthorizationAsync(dbContext, normalizedUrl, encryptedPassword, cancellationToken);

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

        return [.. authorizations.Select(a => new NuGetFeedEndpointCredential(a.FeedUrl, NuGetCredentialProtector.Decrypt(a.EncryptedPassword!)))];
    }

    /// <summary>
    /// Writes/updates a credentialed package source into this machine's default NuGet configuration
    /// for a non-Azure-DevOps feed, then reads it back to verify the write - see design.md's
    /// "verification is local-only" Non-Goal. Returns <see langword="null"/> on success, or the
    /// specific failure otherwise.
    /// </summary>
    private static NuGetCredentialAuthorizeResult? AuthorizeGenericFeed(string normalizedUrl, string token)
    {
        try
        {
            var settings = Settings.LoadDefaultSettings(root: null);
            var sourceProvider = new PackageSourceProvider(settings);
            var credentials = PackageSourceCredential.FromUserInput(normalizedUrl, PlaceholderUsername, token, storePasswordInClearText: false, validAuthenticationTypesText: null);

            var existing = sourceProvider.LoadPackageSources().FirstOrDefault(s => string.Equals(s.Name, normalizedUrl, StringComparison.Ordinal));
            if (existing is not null)
            {
                existing.Credentials = credentials;
                sourceProvider.UpdatePackageSource(existing, updateCredentials: true, updateEnabled: false);
            }
            else
            {
                sourceProvider.AddPackageSource(new PackageSource(normalizedUrl, normalizedUrl) { Credentials = credentials });
            }

            var freshProvider = new PackageSourceProvider(Settings.LoadDefaultSettings(root: null));
            var saved = freshProvider.LoadPackageSources().FirstOrDefault(s => string.Equals(s.Name, normalizedUrl, StringComparison.Ordinal));

            if (saved?.Credentials?.Password != token)
            {
                return new NuGetCredentialAuthorizeResult.VerificationFailed();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new NuGetCredentialAuthorizeResult.ConfigurationUnwritable(ex.Message);
        }

        return null;
    }

    // Checks for the exact file NUGET_NETCORE_PLUGIN_PATHS will be pointed at (see
    // NuGetCredentialProviderLocation.PluginFilePath's own remarks) - not just "some exe exists in
    // this directory," so a partially-extracted or wrong-shaped bundle fails this check the same way
    // a missing one does, rather than reporting installed and failing confusingly later at restore time.
    private static bool IsCredentialProviderInstalled() => File.Exists(NuGetCredentialProviderLocation.PluginFilePath);

    private static async Task UpsertAuthorizationAsync(OutOfTheBoxDbContext dbContext, string normalizedUrl, byte[]? encryptedPassword, CancellationToken cancellationToken)
    {
        var existing = await dbContext.NuGetFeedAuthorizations.FirstOrDefaultAsync(a => a.FeedUrl == normalizedUrl, cancellationToken);
        var updated = new NuGetFeedAuthorization(normalizedUrl, DateTimeOffset.UtcNow, encryptedPassword);

        if (existing is null)
        {
            dbContext.NuGetFeedAuthorizations.Add(updated);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(updated);
        }

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
