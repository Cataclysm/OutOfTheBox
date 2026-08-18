// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Repositories;

namespace OutOfTheBox.UnitTests.Application.Repositories;

/// <summary>
/// Covers <see cref="RepositoryStatsCache"/>'s merge behavior for <see cref="RepositoryStatsCache.SetGitStatus"/>/
/// <see cref="RepositoryStatsCache.SetSize"/> - the two independent cadences (per
/// specs/repository-management's "Repository stats update on two independent cadences") each only
/// ever have one half of a repository's stats, so writing one half must not clobber the other.
/// </summary>
public sealed class RepositoryStatsCacheTests
{
    [Fact]
    public void SetGitStatus_preserves_a_previously_cached_size()
    {
        var cache = new RepositoryStatsCache();
        cache.SetSize("repo", 12345);

        cache.SetGitStatus("repo", new GitStatusSnapshot(IsGitRepository: true, Branch: "main", IsDirty: true, AheadCount: 1, BehindCount: 2, IsRemoteGone: false, Remotes: []));

        var stats = cache.TryGet("repo");
        Assert.NotNull(stats);
        Assert.Equal(12345, stats.TotalSizeBytes);
        Assert.Equal("main", stats.Branch);
        Assert.True(stats.IsDirty);
    }

    [Fact]
    public void SetSize_preserves_a_previously_cached_git_status()
    {
        var cache = new RepositoryStatsCache();
        cache.SetGitStatus("repo", new GitStatusSnapshot(IsGitRepository: true, Branch: "main", IsDirty: false, AheadCount: null, BehindCount: null, IsRemoteGone: false, Remotes: []));

        cache.SetSize("repo", 999);

        var stats = cache.TryGet("repo");
        Assert.NotNull(stats);
        Assert.Equal(999, stats.TotalSizeBytes);
        Assert.Equal("main", stats.Branch);
        Assert.True(stats.IsGitRepository);
    }

    [Fact]
    public void SetGitStatus_on_an_uncached_repository_defaults_size_to_zero()
    {
        var cache = new RepositoryStatsCache();

        cache.SetGitStatus("repo", new GitStatusSnapshot(IsGitRepository: false, Branch: null, IsDirty: false, AheadCount: null, BehindCount: null, IsRemoteGone: false, Remotes: []));

        Assert.Equal(0, cache.TryGet("repo")!.TotalSizeBytes);
    }

    [Fact]
    public void Set_fully_replaces_any_previous_value()
    {
        var cache = new RepositoryStatsCache();
        cache.SetSize("repo", 1);
        cache.SetGitStatus("repo", new GitStatusSnapshot(IsGitRepository: true, Branch: "main", IsDirty: true, AheadCount: null, BehindCount: null, IsRemoteGone: false, Remotes: []));

        cache.Set("repo", new RepositoryStats(2, IsGitRepository: false, Branch: null, IsDirty: false, AheadCount: null, BehindCount: null));

        var stats = cache.TryGet("repo");
        Assert.Equal(2, stats!.TotalSizeBytes);
        Assert.False(stats.IsGitRepository);
        Assert.Null(stats.Branch);
    }
}
