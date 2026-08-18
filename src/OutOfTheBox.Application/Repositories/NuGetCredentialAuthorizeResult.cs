// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// The outcome of an <see cref="INuGetFeedCredentialStore.AuthorizeAsync"/> call - same synchronous,
/// specific-reason-per-failure shape <c>GitCredentialAuthorizeResult</c> uses, per
/// specs/mcp-nuget-credentials' "every tool failure in this capability reports a specific, actionable
/// reason" requirement.
/// </summary>
public abstract record NuGetCredentialAuthorizeResult
{
    /// <summary>The credential was stored and its retrievability was verified.</summary>
    public sealed record Succeeded : NuGetCredentialAuthorizeResult;

    /// <summary>The supplied feed URL is not an absolute <c>http</c>/<c>https</c> URL.</summary>
    public sealed record InvalidFeedUrl : NuGetCredentialAuthorizeResult;

    /// <summary>The write appeared to succeed but reading it back did not return what was just written.</summary>
    public sealed record VerificationFailed : NuGetCredentialAuthorizeResult;

    /// <summary>The machine's NuGet configuration could not be read or written (generic-feed mechanism only).</summary>
    public sealed record ConfigurationUnwritable(string ErrorMessage) : NuGetCredentialAuthorizeResult;

    /// <summary>The feed URL is an Azure DevOps Artifacts feed, but the Azure Artifacts Credential Provider is not installed on this system.</summary>
    public sealed record CredentialProviderNotInstalled : NuGetCredentialAuthorizeResult;
}
