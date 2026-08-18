// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.UnitTests.Domain.Repositories;

public sealed class AzureArtifactsFeedClassifierTests
{
    [Theory]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json")]
    [InlineData("https://PKGS.DEV.AZURE.COM/org/_packaging/feed/nuget/v3/index.json")]
    [InlineData("https://org.pkgs.visualstudio.com/_packaging/feed/nuget/v3/index.json")]
    [InlineData("https://ORG.PKGS.VISUALSTUDIO.COM/_packaging/feed/nuget/v3/index.json")]
    public void Azure_DevOps_Artifacts_hosts_are_classified_as_such(string feedUrl) =>
        Assert.True(AzureArtifactsFeedClassifier.IsAzureDevOpsArtifactsFeed(new Uri(feedUrl)));

    [Theory]
    [InlineData("https://nuget.pkg.github.com/org/index.json")]
    [InlineData("https://api.nuget.org/v3/index.json")]
    [InlineData("https://example.com/pkgs.dev.azure.com/nuget/v3/index.json")]
    [InlineData("https://notpkgs.dev.azure.com/nuget/v3/index.json")]
    [InlineData("https://pkgs.dev.azure.com.evil.example/nuget/v3/index.json")]
    public void Other_hosts_are_not_classified_as_Azure_DevOps_Artifacts(string feedUrl) =>
        Assert.False(AzureArtifactsFeedClassifier.IsAzureDevOpsArtifactsFeed(new Uri(feedUrl)));

    [Fact]
    public void Null_feed_url_throws() =>
        Assert.Throws<ArgumentNullException>(() => AzureArtifactsFeedClassifier.IsAzureDevOpsArtifactsFeed(null!));
}
