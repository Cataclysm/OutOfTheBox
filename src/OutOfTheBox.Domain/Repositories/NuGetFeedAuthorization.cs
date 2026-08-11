// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// A NuGet feed URL an MCP caller has explicitly authorized a credential for, via
/// <c>authorize_nuget_feed</c>. Deliberately holds no username (PAT-only auth - see design.md's "no
/// username parameter" decision) and never the plaintext token. <paramref name="EncryptedPassword"/>
/// is populated only when <see cref="FeedUrl"/> is an Azure DevOps Artifacts feed (per
/// <see cref="AzureArtifactsFeedClassifier"/>) - that mechanism has no external durable store of its
/// own to delegate to (unlike the generic-feed mechanism, whose secret lives only in the machine's
/// NuGet configuration, never here) - see design.md's "durable storage for the Azure DevOps Artifacts
/// mechanism" decision for why this is a deliberate, narrow exception.
/// </summary>
public sealed record NuGetFeedAuthorization(string FeedUrl, DateTimeOffset AuthorizedAtUtc, byte[]? EncryptedPassword);
