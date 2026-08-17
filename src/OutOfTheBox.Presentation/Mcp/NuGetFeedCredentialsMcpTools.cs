// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// MCP <c>authorize_nuget_feed</c>/<c>list_authorized_nuget_feeds</c>/<c>revoke_nuget_feed_authorization</c>
/// tools, per <c>mcp-nuget-credentials</c>' spec: lets a caller store, list, and revoke PAT-based
/// credentials for a NuGet feed, so subsequent <c>dotnet_run</c> calls can authenticate against it
/// transparently with no change to that tool's own contract. All three reuse
/// <see cref="INuGetFeedCredentialStore"/> directly, which internally routes each feed URL to
/// whichever of its two mechanisms applies - the caller never chooses or sees which one.
/// </summary>
[McpServerToolType]
public sealed class NuGetFeedCredentialsMcpTools(INuGetFeedCredentialStore nuGetFeedCredentialStore, IMcpPermissionStore permissionStore)
{
    /// <summary>Stores a personal access token for a NuGet feed.</summary>
    /// <param name="feedUrl">The feed URL, e.g. <c>https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json</c> or <c>https://nuget.pkg.github.com/org/index.json</c>.</param>
    /// <param name="token">The personal access token.</param>
    [McpServerTool(Name = "authorize_nuget_feed")]
    [Description("Stores a personal access token (PAT) for a NuGet feed, so subsequent dotnet_run calls (restore/build/test/pack/push) authenticate against it transparently. Azure DevOps Artifacts feed URLs (pkgs.dev.azure.com, *.pkgs.visualstudio.com) are authorized via the Azure Artifacts Credential Provider; any other feed URL is authorized by writing a credentialed NuGet package source. Verifies the credential before reporting success. Re-authorizing a feed replaces its existing credential.")]
    public async Task<McpAuthorizeNuGetFeedResult> AuthorizeNuGetFeedAsync(
        [Description("The feed URL, e.g. \"https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json\" or \"https://nuget.pkg.github.com/org/index.json\".")] string feedUrl,
        [Description("The personal access token.")] string token)
    {
        permissionStore.EnsureEnabled("authorize_nuget_feed");

        if (string.IsNullOrWhiteSpace(feedUrl) || string.IsNullOrWhiteSpace(token))
        {
            throw new McpException("feedUrl and token must both be supplied.");
        }

        var result = await nuGetFeedCredentialStore.AuthorizeAsync(feedUrl, token, CancellationToken.None);

        return result switch
        {
            NuGetCredentialAuthorizeResult.Succeeded => new McpAuthorizeNuGetFeedResult(true),
            NuGetCredentialAuthorizeResult.InvalidFeedUrl =>
                throw new McpException($"'{feedUrl}' is not a valid absolute http/https URL."),
            NuGetCredentialAuthorizeResult.VerificationFailed =>
                throw new McpException($"The credential for '{feedUrl}' was written but could not be verified as retrievable - the write may not have persisted."),
            NuGetCredentialAuthorizeResult.ConfigurationUnwritable unwritable =>
                throw new McpException($"This machine's NuGet configuration could not be accessed: {unwritable.ErrorMessage}"),
            NuGetCredentialAuthorizeResult.CredentialProviderNotInstalled =>
                throw new McpException($"'{feedUrl}' is an Azure DevOps Artifacts feed, but the Azure Artifacts Credential Provider is not installed on this system."),
            _ => throw new McpException("Unexpected result authorizing the NuGet feed."),
        };
    }

    /// <summary>Lists every feed with a stored credential.</summary>
    [McpServerTool(Name = "list_authorized_nuget_feeds")]
    [Description("Lists every NuGet feed URL with a credential authorized via authorize_nuget_feed, with when it was authorized. Never includes the token itself, which this service never returns from any tool result.")]
    public Task<IReadOnlyList<NuGetFeedAuthorizationSummary>> ListAuthorizedNuGetFeedsAsync()
    {
        permissionStore.EnsureEnabled("list_authorized_nuget_feeds");

        return nuGetFeedCredentialStore.ListAuthorizedFeedsAsync(CancellationToken.None);
    }

    /// <summary>Removes a feed's stored credential.</summary>
    /// <param name="feedUrl">The feed URL to revoke.</param>
    [McpServerTool(Name = "revoke_nuget_feed_authorization")]
    [Description("Removes a NuGet feed's credential, from whichever mechanism backs it, and this service's own record of it. Does not, and cannot, invalidate the personal access token itself - that has to be done at its source directly.")]
    public async Task<McpRevokeNuGetFeedResult> RevokeNuGetFeedAuthorizationAsync(
        [Description("The feed URL to revoke.")] string feedUrl)
    {
        permissionStore.EnsureEnabled("revoke_nuget_feed_authorization");

        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            throw new McpException("feedUrl must be supplied.");
        }

        var result = await nuGetFeedCredentialStore.RevokeAsync(feedUrl, CancellationToken.None);

        return result switch
        {
            NuGetCredentialRevokeResult.Revoked => new McpRevokeNuGetFeedResult(true),
            NuGetCredentialRevokeResult.NothingToRevoke =>
                throw new McpException($"'{feedUrl}' has no credential authorized via authorize_nuget_feed - nothing to revoke."),
            NuGetCredentialRevokeResult.ConfigurationUnwritable unwritable =>
                throw new McpException($"This machine's NuGet configuration could not be accessed: {unwritable.ErrorMessage}"),
            _ => throw new McpException("Unexpected result revoking the NuGet feed credential."),
        };
    }
}

/// <summary>The result of a successful <c>authorize_nuget_feed</c> call.</summary>
/// <param name="Authorized">Always <see langword="true"/> - a failure throws instead of returning a result with this <see langword="false"/>.</param>
public sealed record McpAuthorizeNuGetFeedResult(bool Authorized);

/// <summary>The result of a successful <c>revoke_nuget_feed_authorization</c> call.</summary>
/// <param name="Revoked">Always <see langword="true"/> - a failure throws instead of returning a result with this <see langword="false"/>.</param>
public sealed record McpRevokeNuGetFeedResult(bool Revoked);
