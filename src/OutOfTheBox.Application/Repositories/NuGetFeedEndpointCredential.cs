// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// One decrypted Azure DevOps Artifacts feed credential, as
/// <see cref="INuGetFeedCredentialStore.GetAzureDevOpsArtifactsEndpointCredentialsAsync"/> returns it
/// - deliberately not a Domain type, since a Domain record must never carry a plaintext secret and
/// this one exists only to be serialized into a <c>dotnet_run</c> spawn's
/// <c>VSS_NUGET_EXTERNAL_FEED_ENDPOINTS</c> environment variable and then discarded.
/// </summary>
public sealed record NuGetFeedEndpointCredential(string FeedUrl, string Token);
