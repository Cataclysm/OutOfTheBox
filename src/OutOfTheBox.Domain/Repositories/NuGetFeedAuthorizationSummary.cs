// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// One <c>list_authorized_nuget_feeds</c> result entry - mechanism-agnostic (the caller never learns
/// whether a feed is backed by the Azure Artifacts Credential Provider or a plain NuGet configuration
/// entry), and never contains the token itself.
/// </summary>
public sealed record NuGetFeedAuthorizationSummary(string FeedUrl, DateTimeOffset AuthorizedAtUtc);
