// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Read-only access to a repository's commit history and working-tree state, for the commit graph,
/// commit detail subpage, and file tree's "dirty files only" filter. Split out of the former single
/// <c>IRepositoryManager</c> - see its own remarks - since these four methods are the only ones
/// that never mutate anything (no lock acquisition, no run history), unlike every other slice.
/// </summary>
public interface IRepositoryHistoryReader
{
    /// <summary>
    /// Lists up to <paramref name="take"/> commits (skipping the first <paramref name="skip"/>)
    /// reachable from any branch or tag (<c>git log --all</c>), most-recent-first, for the detail
    /// subpage's commit graph. Returns an empty list for an invalid name or a repository that isn't
    /// a git repository - never throws for those cases.
    /// </summary>
    Task<IReadOnlyList<CommitSummary>> ListCommitsAsync(string name, int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the full detail of one commit (<c>git show</c>) - full message body, separate author/
    /// committer identity, and the list of files it touched - for the commit detail subpage.
    /// <see langword="null"/> for an invalid repository name, a repository that isn't a git
    /// repository, or a hash it doesn't contain.
    /// </summary>
    Task<CommitDetail?> GetCommitDetailAsync(string name, string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the unified diff for one file as changed by <paramref name="hash"/> (<c>git show</c>,
    /// scoped to that file's path), for the commit detail subpage's per-file diff view.
    /// <see langword="null"/> for an invalid repository name, a repository that isn't a git
    /// repository, a hash it doesn't contain, or a path the commit didn't actually touch - as well as
    /// for a file git itself declines to produce a text diff for (e.g. binary content), which
    /// produces no diff body to show.
    /// </summary>
    Task<string?> GetCommitFileDiffAsync(string name, string hash, string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Repo-relative paths of every file with an uncommitted working-tree change - modified, staged,
    /// deleted, or untracked (<c>git status --porcelain</c>) - for the file tree browser's "dirty
    /// files only" filter. A renamed/copied path is reported once, under its current (new) path,
    /// matching what the file tree itself lists. Empty for an invalid repository name, a repository
    /// that isn't a git repository, or a clean working tree.
    /// </summary>
    Task<IReadOnlyList<string>> ListDirtyFilePathsAsync(string name, CancellationToken cancellationToken);
}
