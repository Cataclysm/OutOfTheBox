// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// A NuGet feed URL an MCP caller has explicitly authorized a credential for, via
/// <c>authorize_nuget_feed</c>. Deliberately holds no username (PAT-only auth - see design.md's "no
/// username parameter" decision) and never the plaintext token - <paramref name="EncryptedPassword"/>
/// is a machine-scoped-DPAPI-encrypted copy, populated for every feed regardless of kind. For an
/// Azure DevOps Artifacts feed (per <see cref="AzureArtifactsFeedClassifier"/>) this is the credential's
/// sole durable store (that mechanism has no external store of its own to delegate to). For a generic
/// feed it exists alongside the machine's own NuGet configuration (where the credential is also
/// written) as an independently-durable copy - the same two-stores-kept-in-sync shape
/// <see cref="GitHostAuthorization.EncryptedToken"/> uses for git hosts, both fed by this service's
/// <c>CredentialSyncService</c> - since a service-account-profile-scoped secret was confirmed not to
/// survive a plain uninstall-then-reinstall.
/// </summary>
public sealed record NuGetFeedAuthorization(string FeedUrl, DateTimeOffset AuthorizedAtUtc, byte[]? EncryptedPassword);
