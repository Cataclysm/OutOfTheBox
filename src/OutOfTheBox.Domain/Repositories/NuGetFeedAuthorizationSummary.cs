// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// One <c>list_authorized_nuget_feeds</c> result entry - mechanism-agnostic (the caller never learns
/// whether a feed is backed by the Azure Artifacts Credential Provider or a plain NuGet configuration
/// entry), and never contains the token itself.
/// </summary>
public sealed record NuGetFeedAuthorizationSummary(string FeedUrl, DateTimeOffset AuthorizedAtUtc);
