// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Stores, lists, and revokes PAT-based credentials for a NuGet feed - reachable via the
/// <c>authorize_nuget_feed</c>/<c>list_authorized_nuget_feeds</c>/<c>revoke_nuget_feed_authorization</c>
/// MCP tools. Internally routes each feed URL to one of two mechanisms based on
/// <see cref="AzureArtifactsFeedClassifier"/> (see design.md's "storage is a dual mechanism"
/// decision) - the caller never chooses or sees which one applies. The plaintext token is never
/// persisted for a generic feed (it lives only in the machine's NuGet configuration, encrypted by
/// NuGet's own mechanism); for an Azure DevOps Artifacts feed it is DPAPI-encrypted and persisted by
/// this service itself, since the Azure Artifacts Credential Provider's own mechanism has no
/// durability of its own - see <see cref="NuGetFeedAuthorization"/>'s remarks.
/// </summary>
public interface INuGetFeedCredentialStore
{
    /// <summary>
    /// Stores <paramref name="token"/> as the credential for <paramref name="feedUrl"/>, via whichever
    /// mechanism <see cref="AzureArtifactsFeedClassifier"/> selects for that URL, then verifies the
    /// write locally before reporting success. Replaces any existing credential for the same feed URL.
    /// </summary>
    Task<NuGetCredentialAuthorizeResult> AuthorizeAsync(string feedUrl, string token, CancellationToken cancellationToken);

    /// <summary>Every feed URL previously authorized via <see cref="AuthorizeAsync"/> that has not since been revoked, with its authorization timestamp.</summary>
    Task<IReadOnlyList<NuGetFeedAuthorizationSummary>> ListAuthorizedFeedsAsync(CancellationToken cancellationToken);

    /// <summary>Removes <paramref name="feedUrl"/>'s credential from whichever mechanism backs it, and this service's own record of it.</summary>
    Task<NuGetCredentialRevokeResult> RevokeAsync(string feedUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Every currently-authorized Azure DevOps Artifacts feed, decrypted - the one place plaintext
    /// leaves this store outside of <see cref="AuthorizeAsync"/>'s own write-verification. Used to
    /// build the <c>VSS_NUGET_EXTERNAL_FEED_ENDPOINTS</c> environment variable injected into a
    /// <c>dotnet_run</c> spawn; returns an empty list (never throws) when none are authorized, so a
    /// caller with no Azure DevOps Artifacts feeds sees no behavior change.
    /// </summary>
    Task<IReadOnlyList<NuGetFeedEndpointCredential>> GetAzureDevOpsArtifactsEndpointCredentialsAsync(CancellationToken cancellationToken);
}
