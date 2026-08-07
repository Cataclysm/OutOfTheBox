// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Execution;
using OutOfTheBox.Infrastructure.Repositories;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

/// <summary>
/// Covers <see cref="GitRepositoryStatsProvider"/>'s non-git fast path only - detecting a
/// directory that isn't a git repository at all, without spawning any process, per
/// specs/repository-management's "A non-git directory is listed without a git status" scenario.
/// The git-invoking path (branch/dirty/ahead-behind) needs a real <c>git.exe</c>, which belongs in
/// BehaviorTests per this project's UnitTests convention (no real process spawning there).
/// </summary>
public sealed class GitRepositoryStatsProviderTests : IDisposable
{
    private readonly string _root;

    public GitRepositoryStatsProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "oob-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "readme.txt"), "hello world");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ComputeAsync_reports_a_non_git_directory_without_invoking_git()
    {
        var provider = new GitRepositoryStatsProvider(new UnreachableProcessRunner());

        var stats = await provider.ComputeAsync(_root, CancellationToken.None);

        Assert.False(stats.IsGitRepository);
        Assert.Null(stats.Branch);
        Assert.False(stats.IsDirty);
        Assert.Null(stats.AheadCount);
        Assert.Null(stats.BehindCount);
        Assert.Equal("hello world".Length, stats.TotalSizeBytes);
    }

    private sealed class UnreachableProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A non-git directory must not spawn git.exe.");
    }
}
