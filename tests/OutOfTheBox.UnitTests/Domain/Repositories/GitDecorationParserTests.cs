// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.UnitTests.Domain.Repositories;

public sealed class GitDecorationParserTests
{
    private static readonly IReadOnlySet<string> OriginOnly = new HashSet<string> { "origin" };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_null_decorations_produce_no_refs(string? decorations) =>
        Assert.Empty(GitDecorationParser.Parse(decorations, OriginOnly));

    [Fact]
    public void Bare_HEAD_token_is_skipped() => Assert.Empty(GitDecorationParser.Parse("HEAD", OriginOnly));

    [Fact]
    public void HEAD_arrow_branch_is_the_current_local_branch()
    {
        var refs = GitDecorationParser.Parse("HEAD -> main", OriginOnly);

        var single = Assert.Single(refs);
        Assert.Equal("main", single.Name);
        Assert.Equal(CommitRefKind.LocalBranch, single.Kind);
        Assert.True(single.IsCurrent);
    }

    [Fact]
    public void Tag_prefix_is_parsed_as_a_tag()
    {
        var refs = GitDecorationParser.Parse("tag: v1.0", OriginOnly);

        var single = Assert.Single(refs);
        Assert.Equal("v1.0", single.Name);
        Assert.Equal(CommitRefKind.Tag, single.Kind);
        Assert.False(single.IsCurrent);
    }

    [Fact]
    public void A_slash_prefixed_by_a_known_remote_name_is_a_remote_branch()
    {
        var refs = GitDecorationParser.Parse("origin/main", OriginOnly);

        var single = Assert.Single(refs);
        Assert.Equal("origin/main", single.Name);
        Assert.Equal(CommitRefKind.RemoteBranch, single.Kind);
    }

    [Fact]
    public void A_local_branch_containing_a_slash_is_not_mistaken_for_a_remote()
    {
        // "feature" is not a configured remote name, so "feature/x" must be treated as a local
        // branch despite having the same slash-shape as a remote-tracking ref.
        var refs = GitDecorationParser.Parse("feature/x", OriginOnly);

        var single = Assert.Single(refs);
        Assert.Equal("feature/x", single.Name);
        Assert.Equal(CommitRefKind.LocalBranch, single.Kind);
    }

    [Fact]
    public void A_plain_local_branch_with_no_slash_is_a_local_branch()
    {
        var refs = GitDecorationParser.Parse("develop", OriginOnly);

        var single = Assert.Single(refs);
        Assert.Equal("develop", single.Name);
        Assert.Equal(CommitRefKind.LocalBranch, single.Kind);
    }

    [Fact]
    public void A_remotes_own_symbolic_HEAD_pointer_is_excluded()
    {
        // Real `git log --format=%D` output on a freshly cloned repo, verified against this
        // repository's own history: "HEAD -> main, origin/main, origin/HEAD" - origin/HEAD is the
        // remote's symbolic default-branch pointer, not an actual branch, and must not be shown as
        // a "origin/HEAD" ref pill on the commit.
        var refs = GitDecorationParser.Parse("HEAD -> main, origin/main, origin/HEAD", OriginOnly);

        Assert.Equal(2, refs.Count);
        Assert.DoesNotContain(refs, r => r.Name == "origin/HEAD");
    }

    [Fact]
    public void A_full_realistic_decoration_string_parses_every_token()
    {
        var refs = GitDecorationParser.Parse("HEAD -> main, origin/main, origin/feature/x, tag: v2.0", OriginOnly);

        Assert.Equal(4, refs.Count);
        Assert.Contains(refs, r => r is { Name: "main", Kind: CommitRefKind.LocalBranch, IsCurrent: true });
        Assert.Contains(refs, r => r is { Name: "origin/main", Kind: CommitRefKind.RemoteBranch, IsCurrent: false });
        Assert.Contains(refs, r => r is { Name: "origin/feature/x", Kind: CommitRefKind.RemoteBranch, IsCurrent: false });
        Assert.Contains(refs, r => r is { Name: "v2.0", Kind: CommitRefKind.Tag, IsCurrent: false });
    }
}
