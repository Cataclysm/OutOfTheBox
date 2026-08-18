// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using NuGet.Configuration;
using OutOfTheBox.Application.Repositories;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// Reads/writes a generic (non-Azure-DevOps-Artifacts) NuGet feed's credential in this machine's own
/// NuGet configuration - shared by <see cref="NuGetFeedCredentialStore"/> (the operator/MCP-facing
/// authorize path) and <c>CredentialSyncService</c> (the periodic background repair path), the same
/// "one write path, two callers" shape <see cref="GitCredentialWriter"/> uses for git hosts.
/// </summary>
internal static class NuGetFeedConfigWriter
{
    // Any non-empty username works for both GitHub Packages and Azure Artifacts when the password is
    // a valid PAT (see design.md's "no username parameter" decision - unverified, folded into live
    // verification). Fixed and never caller-supplied, so the feed URL alone is always the match key.
    private const string PlaceholderUsername = "nuget";

    /// <summary>The password currently stored in this machine's NuGet configuration for <paramref name="normalizedUrl"/>, or <see langword="null"/> if absent or unreadable.</summary>
    public static string? ReadCurrentPassword(string normalizedUrl)
    {
        try
        {
            var provider = new PackageSourceProvider(Settings.LoadDefaultSettings(root: null));
            var saved = provider.LoadPackageSources().FirstOrDefault(s => string.Equals(s.Name, normalizedUrl, StringComparison.Ordinal));
            return saved?.Credentials?.Password;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes/updates a credentialed package source into this machine's default NuGet configuration
    /// for a non-Azure-DevOps feed, then reads it back to verify the write - see design.md's
    /// "verification is local-only" Non-Goal. Returns <see langword="null"/> on success, or the
    /// specific failure otherwise.
    /// </summary>
    public static NuGetCredentialAuthorizeResult? WriteAndVerify(string normalizedUrl, string token)
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

            if (ReadCurrentPassword(normalizedUrl) != token)
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
}
