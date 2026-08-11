// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

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
        var provider = new GitRepositoryStatsProvider(new UnreachableProcessRunner(), new NoOpGitCredentialStore(), NullLogger<GitRepositoryStatsProvider>.Instance);

        var stats = await provider.ComputeAsync(_root, CancellationToken.None);

        Assert.False(stats.IsGitRepository);
        Assert.Null(stats.Branch);
        Assert.False(stats.IsDirty);
        Assert.Null(stats.AheadCount);
        Assert.Null(stats.BehindCount);
        Assert.Equal("hello world".Length, stats.TotalSizeBytes);
    }

    [Fact]
    public async Task ComputeGitStatusAsync_does_not_throw_when_git_exe_is_unreachable()
    {
        // Regression test for the bug that made repository stats stop updating entirely: a
        // Win32Exception from an unreachable git.exe (e.g. missing from the service account's PATH)
        // previously propagated straight out of this method, which - left uncaught two layers up in
        // RepositoryStatsSampler - crashed the whole BackgroundService (and, by default, the host).
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var provider = new GitRepositoryStatsProvider(new Win32ExceptionProcessRunner(), new NoOpGitCredentialStore(), NullLogger<GitRepositoryStatsProvider>.Instance);

        var status = await provider.ComputeGitStatusAsync(_root, CancellationToken.None);

        Assert.True(status.IsGitRepository);
        Assert.Null(status.Branch);
        Assert.False(status.IsDirty);
        Assert.Null(status.AheadCount);
        Assert.Null(status.BehindCount);
        Assert.False(status.IsRemoteGone);
        Assert.Empty(status.Remotes);
        Assert.False(status.NeedsCredential);
    }

    private sealed class UnreachableProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken, Action<int>? onStarted = null) =>
            throw new InvalidOperationException("A non-git directory must not spawn git.exe.");
    }

    private sealed class Win32ExceptionProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, IProcessOutputSink outputSink, CancellationToken cancellationToken, Action<int>? onStarted = null) =>
            throw new Win32Exception("The system cannot find the file specified.");
    }

    /// <summary>No credential ever needs attention - these tests never resolve a real `origin` remote, so this is never actually called.</summary>
    private sealed class NoOpGitCredentialStore : IGitCredentialStore
    {
        public Task<GitCredentialAuthorizeResult> AuthorizeAsync(string host, string token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task<IReadOnlyList<GitHostAuthorizationSummary>> ListAuthorizedHostsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task<GitCredentialRevokeResult> RevokeAsync(string host, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task RecordOutcomeAsync(string host, bool succeeded, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task<GitHostCredentialHealth?> GetHealthAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<GitHostCredentialHealth?>(null);

        public Task RecordRepositoryOutcomeAsync(string repositoryPath, bool succeeded, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");

        public Task<RepositoryCredentialHealth?> GetRepositoryHealthAsync(string repositoryPath, CancellationToken cancellationToken) =>
            Task.FromResult<RepositoryCredentialHealth?>(null);

        public Task RenameRepositoryHealthAsync(string oldRepositoryPath, string newRepositoryPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not exercised by these tests.");
    }
}
