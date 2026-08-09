// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Lists, deletes, and renames files/folders within one repository's own directory tree, for the
/// dashboard's file tree browser - per specs/repository-management's "Repository detail provides a
/// file tree browser" requirement. Every method confines <c>relativePath</c> to within the named
/// repository's own directory the same way <c>file-transfer</c> confines a transfer request (root
/// -&gt; repository, then repository -&gt; sub-path) - never able to read, delete, or rename anything
/// outside it. Listing is read-only and does not acquire the per-repository command lock (matching
/// <c>git status</c>'s own precedent); delete and rename do, the same as every other mutating
/// repository action, since they touch the filesystem a concurrent build/git command could also be
/// touching.
/// </summary>
public interface IRepositoryFileBrowser
{
    /// <summary>Lists the immediate contents of <paramref name="relativePath"/> within the named repository (empty string for the repository root itself). Returns an empty list if the name or path is invalid, or the directory doesn't exist.</summary>
    Task<IReadOnlyList<RepositoryFileEntry>> ListDirectoryAsync(string repositoryName, string relativePath, CancellationToken cancellationToken);

    /// <summary>Deletes the file or folder (recursively) at <paramref name="relativePath"/>. Rejects an empty path - the repository root itself can't be removed through this, only through <see cref="IRepositoryManager.DeleteAsync"/>.</summary>
    Task<RepositoryFileActionResult> DeleteAsync(string repositoryName, string relativePath, CancellationToken cancellationToken);

    /// <summary>Renames the file or folder at <paramref name="relativePath"/> to <paramref name="newName"/> within the same parent directory - <paramref name="newName"/> may not itself contain a path separator (no moving across directories through this).</summary>
    Task<RepositoryFileActionResult> RenameAsync(string repositoryName, string relativePath, string newName, CancellationToken cancellationToken);

    /// <summary>Resolves the confined absolute path of an existing <em>file</em> (not a directory) at <paramref name="relativePath"/>, for the dashboard's file-download endpoint to stream. <see langword="null"/> if the name/path is invalid, escapes the repository, or doesn't resolve to an existing file.</summary>
    Task<string?> ResolveConfinedFilePathAsync(string repositoryName, string relativePath, CancellationToken cancellationToken);
}
