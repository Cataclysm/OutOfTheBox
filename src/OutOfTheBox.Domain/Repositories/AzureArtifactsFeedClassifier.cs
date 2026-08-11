// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// Classifies a NuGet feed URL as an Azure DevOps Artifacts feed or not - pure string/host matching,
/// no IO. Drives which of the two credential mechanisms <c>authorize_nuget_feed</c>/
/// <c>list_authorized_nuget_feeds</c>/<c>revoke_nuget_feed_authorization</c> use for a given feed URL,
/// per design.md's "storage is a dual mechanism, chosen per feed URL pattern" decision. Deliberately a
/// pure function of the URL rather than a persisted discriminator - the classification never needs to
/// be re-derived differently for the same URL later.
/// </summary>
public static class AzureArtifactsFeedClassifier
{
    /// <summary>Whether <paramref name="feedUrl"/> is an Azure DevOps Artifacts feed (<c>pkgs.dev.azure.com</c> or a <c>*.pkgs.visualstudio.com</c> host).</summary>
    public static bool IsAzureDevOpsArtifactsFeed(Uri feedUrl)
    {
        ArgumentNullException.ThrowIfNull(feedUrl);

        var host = feedUrl.Host;
        return host.Equals("pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase);
    }
}
