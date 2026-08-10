// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Lists, searches, inspects, deletes, and renames files/folders within one repository's own
/// directory tree - for the dashboard's file tree browser (per specs/repository-management's
/// "Repository detail provides a file tree browser" requirement) and, since
/// specs/mcp-file-management, the MCP <c>find_files</c>/<c>get_file_info</c>/<c>delete_path</c>
/// tools. Every method confines <c>relativePath</c> to within the named repository's own directory
/// the same way <c>file-transfer</c> confines a transfer request (root -&gt; repository, then
/// repository -&gt; sub-path) - never able to read, delete, or rename anything outside it. Listing,
/// searching, and metadata are read-only and do not acquire the per-repository command lock
/// (matching <c>git status</c>'s own precedent); delete and rename do, the same as every other
/// mutating repository action, since they touch the filesystem a concurrent build/git command could
/// also be touching.
/// </summary>
public interface IRepositoryFileBrowser
{
    /// <summary>Lists the immediate contents of <paramref name="relativePath"/> within the named repository (empty string for the repository root itself). Returns an empty list if the name or path is invalid, or the directory doesn't exist.</summary>
    Task<IReadOnlyList<RepositoryFileEntry>> ListDirectoryAsync(string repositoryName, string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Recursively searches the named repository's own directory tree for every file and folder
    /// whose path relative to the repository root matches <paramref name="pattern"/> (<c>*</c>,
    /// <c>**</c>, and <c>?</c> wildcards; matches everything if <paramref name="pattern"/> is empty
    /// or whitespace). Returns an empty list if the repository name is invalid. The result is capped
    /// (see <c>ServiceOptions.McpMaxFindFilesResults</c>); <see cref="RepositoryEntrySearchResult.Truncated"/>
    /// indicates the cap was reached.
    /// </summary>
    Task<RepositoryEntrySearchResult> FindEntriesAsync(string repositoryName, string pattern, CancellationToken cancellationToken);

    /// <summary>Returns full filesystem metadata for the entry at <paramref name="relativePath"/> within the named repository. <see langword="null"/> if the name/path is invalid, escapes the repository, or doesn't resolve to an existing entry.</summary>
    Task<RepositoryEntryMetadata?> GetMetadataAsync(string repositoryName, string relativePath, CancellationToken cancellationToken);

    /// <summary>Deletes the file or folder (recursively) at <paramref name="relativePath"/>. Rejects an empty path - the repository root itself can't be removed through this, only through <see cref="IRepositoryManager.DeleteAsync"/>.</summary>
    Task<RepositoryFileActionResult> DeleteAsync(string repositoryName, string relativePath, CancellationToken cancellationToken);

    /// <summary>Renames the file or folder at <paramref name="relativePath"/> to <paramref name="newName"/> within the same parent directory - <paramref name="newName"/> may not itself contain a path separator (no moving across directories through this).</summary>
    Task<RepositoryFileActionResult> RenameAsync(string repositoryName, string relativePath, string newName, CancellationToken cancellationToken);

    /// <summary>Resolves the confined absolute path of an existing <em>file</em> (not a directory) at <paramref name="relativePath"/>, for the dashboard's file-download endpoint to stream. <see langword="null"/> if the name/path is invalid, escapes the repository, or doesn't resolve to an existing file.</summary>
    Task<string?> ResolveConfinedFilePathAsync(string repositoryName, string relativePath, CancellationToken cancellationToken);
}
