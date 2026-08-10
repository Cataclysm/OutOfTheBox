// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.Presentation.Dashboard.CodePreview;
using Bunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="CommitDetailPage"/> via bUnit - covers the page title/heading now
/// being the commit subject, author/committer avatars (Gravatar image plus its vendored fallback
/// icon), and parents now showing each parent's own subject alongside its hash link, not just a
/// bare comma-separated hash list.
/// </summary>
public sealed class CommitDetailPageComponentTests : DashboardComponentTestContext, IDisposable
{
    private readonly IRepositoryStatsEventBus _repositoryStatsEventBus = new InMemoryRepositoryStatsEventBus(NullLogger<InMemoryRepositoryStatsEventBus>.Instance);
    private readonly FakeRepositoryManager _repositoryManager = new();

    public CommitDetailPageComponentTests()
    {
        // CommitFileDiffDialog is always embedded in the page's own markup (mounted on click, not
        // conditionally rendered) - same reasoning as FileTreeComponentTests registering this.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment());
        Services.AddSingleton<IRepositoryManager>(_repositoryManager);
        Services.AddSingleton(_repositoryStatsEventBus);
        Services.AddScoped<ICodePreviewInterop, CodePreviewInterop>();
    }

    [Fact]
    public void Heading_and_page_title_show_the_commit_subject_not_the_hash()
    {
        _repositoryManager.Detail = SampleDetail(subject: "Fix the thing that was broken");

        var cut = Render<CommitDetailPage>(parameters => parameters
            .Add(p => p.Name, "repo")
            .Add(p => p.Hash, "abc1234"));

        cut.WaitForAssertion(() => Assert.Contains("Fix the thing that was broken", cut.Find("h1").TextContent));
    }

    [Fact]
    public void Author_and_committer_avatars_use_a_gravatar_url_hashed_from_their_email()
    {
        _repositoryManager.Detail = SampleDetail(authorEmail: "AUTHOR@Example.com ", committerEmail: "committer@example.com");

        var cut = Render<CommitDetailPage>(parameters => parameters
            .Add(p => p.Name, "repo")
            .Add(p => p.Hash, "abc1234"));

        cut.WaitForAssertion(() =>
        {
            var images = cut.FindAll(".avatar-image");
            Assert.Equal(2, images.Count);
            // Gravatar hashes the trimmed, lowercased email - "AUTHOR@Example.com " and
            // "author@example.com" must therefore hash identically.
            Assert.Contains(GravatarHash("author@example.com"), images[0].GetAttribute("src"));
            Assert.Contains(GravatarHash("committer@example.com"), images[1].GetAttribute("src"));
            // Every avatar renders its fallback markup up front (hidden via inline style, swapped in
            // by the plain HTML onerror attribute - no Blazor interop involved) - not injected later,
            // since bUnit has no real browser behind it to ever fire that event.
            Assert.Equal(2, cut.FindAll(".avatar-fallback").Count);
        });
    }

    [Fact]
    public void Author_and_committer_names_are_bold_emails_are_mailto_links_and_dates_are_on_their_own_line()
    {
        _repositoryManager.Detail = SampleDetail(authorEmail: "author@example.com", committerEmail: "committer@example.com");

        var cut = Render<CommitDetailPage>(parameters => parameters
            .Add(p => p.Name, "repo")
            .Add(p => p.Hash, "abc1234"));

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".commit-person");
            Assert.Equal(2, rows.Count);

            foreach (var row in rows)
            {
                Assert.NotNull(row.QuerySelector("strong"));
                Assert.NotEmpty(row.QuerySelectorAll(".commit-person-date"));
            }

            var mailtoLinks = cut.FindAll(".commit-person a");
            Assert.Equal(2, mailtoLinks.Count);
            Assert.Equal("mailto:author@example.com", mailtoLinks[0].GetAttribute("href"));
            Assert.Equal("mailto:committer@example.com", mailtoLinks[1].GetAttribute("href"));

            // The date used to be rendered inline as "... on <date>" - removed per direct instruction.
            Assert.DoesNotContain(" on ", cut.Markup);
        });
    }

    [Fact]
    public void A_root_commit_shows_no_parents()
    {
        _repositoryManager.Detail = SampleDetail(parents: []);

        var cut = Render<CommitDetailPage>(parameters => parameters
            .Add(p => p.Name, "repo")
            .Add(p => p.Hash, "abc1234"));

        cut.WaitForAssertion(() => Assert.Contains("None (root commit)", cut.Markup));
    }

    [Fact]
    public void Each_parent_shows_its_own_hash_link_and_subject()
    {
        _repositoryManager.Detail = SampleDetail(parents:
        [
            new CommitParentInfo("parent1hash", "parent1", "First parent's subject"),
            new CommitParentInfo("parent2hash", "parent2", "Second parent's subject"),
        ]);

        var cut = Render<CommitDetailPage>(parameters => parameters
            .Add(p => p.Name, "repo")
            .Add(p => p.Hash, "abc1234"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("First parent's subject", cut.Markup);
            Assert.Contains("Second parent's subject", cut.Markup);
            var links = cut.FindAll(".commit-parent-list a");
            Assert.Equal(2, links.Count);
            Assert.Equal("repository-management/repo/commits/parent1hash", links[0].GetAttribute("href"));
            Assert.Equal("repository-management/repo/commits/parent2hash", links[1].GetAttribute("href"));
        });
    }

    // Mirrors Avatar.razor's own GravatarHash - MD5 is Gravatar's documented hash-the-email API
    // contract, not a security use; see that method's own remarks.
#pragma warning disable CA5351
    private static string GravatarHash(string email) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(email)));
#pragma warning restore CA5351

    private static CommitDetail SampleDetail(
        string subject = "Sample subject",
        string authorEmail = "author@example.com",
        string committerEmail = "committer@example.com",
        IReadOnlyList<CommitParentInfo>? parents = null) => new(
        Hash: "abc1234def",
        ShortHash: "abc1234",
        Parents: parents ?? [new CommitParentInfo("parenthash", "parent1", "Parent subject")],
        AuthorName: "An Author",
        AuthorEmail: authorEmail,
        AuthorDate: DateTimeOffset.UtcNow,
        CommitterName: "A Committer",
        CommitterEmail: committerEmail,
        CommitterDate: DateTimeOffset.UtcNow,
        Subject: subject,
        Body: string.Empty,
        Refs: [],
        Files: []);

    /// <inheritdoc />
    public new void Dispose() => base.Dispose();

    private sealed class FakeRepositoryManager : IRepositoryManager
    {
        public CommitDetail? Detail { get; set; }

        public Task<CommitDetail?> GetCommitDetailAsync(string name, string hash, CancellationToken cancellationToken) =>
            Task.FromResult(Detail);

        public Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositorySummary>>([]);

        public Task<RepositoryActionResult> CloneAsync(string url, string name, string? branch, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryActionResult> DeleteAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> PullAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> PushAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> ForcePushAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> FetchAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> CleanAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> RenameAsync(string name, string newName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetCloneSourceUrlAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RepositoryBranch>> ListBranchesAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> SwitchBranchAsync(string name, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListRemoteBranchesAsync(string url, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CommitSummary>> ListCommitsAsync(string name, int skip, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RepositoryGitActionResult> CheckoutCommitAsync(string name, string hash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetCommitFileDiffAsync(string name, string hash, string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListDirtyFilePathsAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
