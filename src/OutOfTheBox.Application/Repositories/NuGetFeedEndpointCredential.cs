// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// One decrypted Azure DevOps Artifacts feed credential, as
/// <see cref="INuGetFeedCredentialStore.GetAzureDevOpsArtifactsEndpointCredentialsAsync"/> returns it
/// - deliberately not a Domain type, since a Domain record must never carry a plaintext secret and
/// this one exists only to be serialized into a <c>dotnet_run</c> spawn's
/// <c>VSS_NUGET_EXTERNAL_FEED_ENDPOINTS</c> environment variable and then discarded.
/// </summary>
public sealed record NuGetFeedEndpointCredential(string FeedUrl, string Token);
