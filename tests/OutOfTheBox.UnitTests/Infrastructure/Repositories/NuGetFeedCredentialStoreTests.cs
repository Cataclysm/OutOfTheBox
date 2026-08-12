// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

/// <summary>
/// Exercises <see cref="NuGetFeedCredentialStore"/>'s validation, Azure DevOps Artifacts routing,
/// and DB-only metadata behavior against a real in-memory SQLite database - never the generic-feed
/// mechanism's real writes to this machine's own NuGet configuration (a real file under
/// <c>%AppData%\NuGet\</c>), per this project's "no real process/IO spawning in UnitTests"
/// convention (see <c>RepositoryManagerTests</c>' own doc comment) - that mechanism's real
/// read/write round-trip is a BehaviorTests concern instead.
/// </summary>
public sealed class NuGetFeedCredentialStoreTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly DpapiCredentialProtector _credentialProtector = new(NullLogger<DpapiCredentialProtector>.Instance);
    private readonly NuGetFeedCredentialStore _store;

    public NuGetFeedCredentialStoreTests()
    {
        var services = new ServiceCollection();
        services.AddTransient(_ => _dbContextFactory.CreateContext());
        _serviceProvider = services.BuildServiceProvider();
        _store = new NuGetFeedCredentialStore(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryCredentialEventBus(NullLogger<InMemoryCredentialEventBus>.Instance),
            _credentialProtector);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url at all")]
    [InlineData("ftp://example.com/feed")]
    [InlineData("relative/path")]
    public async Task Authorize_rejects_an_invalid_feed_URL_without_touching_anything(string feedUrl)
    {
        var result = await _store.AuthorizeAsync(feedUrl, "token", CancellationToken.None);

        Assert.IsType<NuGetCredentialAuthorizeResult.InvalidFeedUrl>(result);
    }

    [Fact]
    public async Task Authorize_against_an_Azure_DevOps_Artifacts_feed_fails_specifically_when_the_credential_provider_is_not_installed()
    {
        // The credential provider is never installed in this test environment (NuGetCredentialProviderLocation
        // points under AppContext.BaseDirectory, which has no CredentialProviders subfolder here) - this
        // genuinely exercises the not-installed path end to end, not a mock.
        var result = await _store.AuthorizeAsync(
            "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json", "token", CancellationToken.None);

        Assert.IsType<NuGetCredentialAuthorizeResult.CredentialProviderNotInstalled>(result);
    }

    [Fact]
    public async Task Listing_before_anything_is_authorized_is_empty()
    {
        var result = await _store.ListAuthorizedFeedsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Listing_reports_a_seeded_authorization_with_no_token_value()
    {
        await SeedAzureDevOpsArtifactsAuthorizationAsync("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json", "s3cr3t-pat");

        var result = await _store.ListAuthorizedFeedsAsync(CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json", summary.FeedUrl);
    }

    [Fact]
    public async Task Revoking_an_Azure_DevOps_Artifacts_feed_removes_it_without_touching_NuGet_configuration()
    {
        const string feedUrl = "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json";
        await SeedAzureDevOpsArtifactsAuthorizationAsync(feedUrl, "s3cr3t-pat");

        var result = await _store.RevokeAsync(feedUrl, CancellationToken.None);

        Assert.IsType<NuGetCredentialRevokeResult.Revoked>(result);
        Assert.Empty(await _store.ListAuthorizedFeedsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Revoking_a_feed_that_was_never_authorized_reports_nothing_to_revoke()
    {
        var result = await _store.RevokeAsync("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json", CancellationToken.None);

        Assert.IsType<NuGetCredentialRevokeResult.NothingToRevoke>(result);
    }

    [Fact]
    public async Task Revoking_an_invalid_feed_URL_reports_nothing_to_revoke_rather_than_throwing()
    {
        var result = await _store.RevokeAsync("not a url", CancellationToken.None);

        Assert.IsType<NuGetCredentialRevokeResult.NothingToRevoke>(result);
    }

    [Fact]
    public async Task Azure_DevOps_Artifacts_endpoint_credentials_decrypt_to_the_originally_authorized_token()
    {
        const string feedUrl = "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json";
        await SeedAzureDevOpsArtifactsAuthorizationAsync(feedUrl, "s3cr3t-pat");

        var credentials = await _store.GetAzureDevOpsArtifactsEndpointCredentialsAsync(CancellationToken.None);

        var credential = Assert.Single(credentials);
        Assert.Equal(feedUrl, credential.FeedUrl);
        Assert.Equal("s3cr3t-pat", credential.Token);
    }

    [Fact]
    public async Task Endpoint_credentials_are_empty_when_nothing_is_authorized()
    {
        var credentials = await _store.GetAzureDevOpsArtifactsEndpointCredentialsAsync(CancellationToken.None);

        Assert.Empty(credentials);
    }

    /// <summary>
    /// Inserts a row directly, bypassing <see cref="NuGetFeedCredentialStore.AuthorizeAsync"/> - its
    /// own Azure DevOps Artifacts path can't be driven end to end in this test environment, since the
    /// credential provider is never actually installed here (see the dedicated test above for that).
    /// </summary>
    private async Task SeedAzureDevOpsArtifactsAuthorizationAsync(string feedUrl, string token)
    {
        await using var dbContext = _dbContextFactory.CreateContext();
        dbContext.NuGetFeedAuthorizations.Add(new NuGetFeedAuthorization(feedUrl, DateTimeOffset.UtcNow, _credentialProtector.Encrypt(token)));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _serviceProvider.Dispose();
        _dbContextFactory.Dispose();
    }
}
