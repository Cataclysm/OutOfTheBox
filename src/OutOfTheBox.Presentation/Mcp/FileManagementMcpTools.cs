// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// MCP <c>find_files</c>/<c>get_file_info</c>/<c>delete_path</c> tools, per
/// <c>mcp-file-management</c>'s spec: real, current filesystem search/inspection/deletion within one
/// named repository - preferred over <c>git_run</c> for file-listing/metadata questions, since git's
/// own view (<c>git ls-files</c>, <c>git status</c>, ...) only reflects tracked/index state, not
/// what's actually on disk right now (an untracked or <c>.gitignore</c>d file is invisible to git,
/// but very much real). All three reuse <see cref="IRepositoryFileBrowser"/> directly - the same
/// confinement, locking, and (for delete) retry/history-recording behavior the dashboard's own file
/// tree browser already relies on.
/// </summary>
[McpServerToolType]
public sealed class FileManagementMcpTools(IWorkingDirectoryResolver workingDirectoryResolver, IRepositoryFileBrowser repositoryFileBrowser)
{
    /// <summary>Searches a repository's real filesystem for files/folders matching a glob pattern.</summary>
    /// <param name="repository">The repository name (as returned by <c>list_repositories</c>).</param>
    /// <param name="pattern">Glob pattern (<c>*</c>, <c>**</c>, <c>?</c>) matched against each entry's path relative to the repository root. Omit to match everything.</param>
    [McpServerTool]
    [Description("Recursively searches a repository's real, current filesystem for files and directories matching a glob pattern (*, **, ? - e.g. \"**/*.cs\" for every C# file anywhere, \"*.md\" for Markdown files directly in the repository root). Prefer this over running git commands (git_run with \"ls-files\"/\"status\") for file-listing questions - git's own view only reflects tracked/index state, not what's actually on disk (an untracked or .gitignore'd file is invisible to git but still found here). Rejects an invalid or escaping repository name. Results are capped; a truncated result means the pattern matched more than the cap and should be narrowed.")]
    public async Task<McpFindFilesResult> FindFilesAsync(
        [Description("The repository name (as returned by list_repositories).")] string repository,
        [Description("Glob pattern (*, **, ?) matched against each entry's path relative to the repository root. Omit to match everything.")] string? pattern = null)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new McpException("repository must be supplied.");
        }

        var repositoryResolution = workingDirectoryResolver.Resolve(repository);
        if (!repositoryResolution.IsAllowed)
        {
            throw new McpException($"repository '{repository}' is outside the configured root.");
        }

        var result = await repositoryFileBrowser.FindEntriesAsync(repository, pattern ?? string.Empty, CancellationToken.None);

        return new McpFindFilesResult(
            [.. result.Entries.Select(e => new McpRepositoryEntry(e.RelativePath, e.IsDirectory, e.SizeBytes, e.LastModifiedUtc))],
            result.Truncated);
    }

    /// <summary>Returns real filesystem metadata (type, size, attributes, dates, owner, lock status) for one entry within a repository.</summary>
    /// <param name="repository">The repository name (as returned by <c>list_repositories</c>).</param>
    /// <param name="path">The entry's path, relative to the named repository.</param>
    [McpServerTool]
    [Description("Returns real filesystem metadata for one file or directory within a repository: type, size, attributes (read-only, hidden, ...), created/modified/accessed timestamps, owner, and (files only) whether it's currently locked by another process. Prefer this over running git commands for file-metadata questions - git has no concept of filesystem attributes, ownership, or locks at all. Rejects a path that escapes the named repository or an entry that doesn't exist.")]
    public async Task<McpFileInfoResult> GetFileInfoAsync(
        [Description("The repository name (as returned by list_repositories).")] string repository,
        [Description("The entry's path, relative to the named repository.")] string path)
    {
        if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("repository and path must both be supplied.");
        }

        var repositoryResolution = workingDirectoryResolver.Resolve(repository);
        if (!repositoryResolution.IsAllowed)
        {
            throw new McpException($"repository '{repository}' is outside the configured root.");
        }

        var repositoryRoot = repositoryResolution.ResolvedPath!;
        var pathResolution = workingDirectoryResolver.ResolveWithinRoot(repositoryRoot, path);
        if (!pathResolution.IsAllowed)
        {
            throw new McpException($"path '{path}' escapes repository '{repository}'.");
        }

        var metadata = await repositoryFileBrowser.GetMetadataAsync(repository, path, CancellationToken.None)
            ?? throw new McpException($"'{path}' does not exist in repository '{repository}'.");

        return new McpFileInfoResult(
            metadata.RelativePath,
            metadata.IsDirectory,
            metadata.SizeBytes,
            metadata.Attributes.ToString(),
            metadata.CreatedUtc,
            metadata.LastModifiedUtc,
            metadata.LastAccessedUtc,
            metadata.Owner,
            metadata.IsLocked);
    }

    /// <summary>Deletes a file or directory (recursively) within a repository.</summary>
    /// <param name="repository">The repository name (as returned by <c>list_repositories</c>).</param>
    /// <param name="path">The file or directory's path, relative to the named repository.</param>
    [McpServerTool]
    [Description("Deletes a file, or a directory and everything under it, within a repository. To delete an entire repository, use delete_repository instead - this tool rejects an empty path. Rejects a path that escapes the named repository, one that doesn't exist, or one in a repository with another run currently in flight - each with a distinct, specific error.")]
    public async Task<McpDeletePathResult> DeletePathAsync(
        [Description("The repository name (as returned by list_repositories).")] string repository,
        [Description("The file or directory's path, relative to the named repository.")] string path)
    {
        if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("repository and path must both be supplied.");
        }

        var repositoryResolution = workingDirectoryResolver.Resolve(repository);
        if (!repositoryResolution.IsAllowed)
        {
            throw new McpException($"repository '{repository}' is outside the configured root.");
        }

        var repositoryRoot = repositoryResolution.ResolvedPath!;
        var pathResolution = workingDirectoryResolver.ResolveWithinRoot(repositoryRoot, path);
        if (!pathResolution.IsAllowed)
        {
            throw new McpException($"path '{path}' escapes repository '{repository}'.");
        }

        var result = await repositoryFileBrowser.DeleteAsync(repository, path, CancellationToken.None);

        return result switch
        {
            RepositoryFileActionResult.Succeeded => new McpDeletePathResult(true),
            RepositoryFileActionResult.Failed failed => throw new McpException($"Failed to delete '{path}' in repository '{repository}': {failed.ErrorMessage}"),
            RepositoryFileActionResult.Rejected { Reason: RepositoryActionRejectionReason.NotFound } =>
                throw new McpException($"'{path}' does not exist in repository '{repository}'."),
            RepositoryFileActionResult.Rejected { Reason: RepositoryActionRejectionReason.Busy } rejected =>
                throw new McpException($"repository '{repository}' is busy with in-flight run '{rejected.ConflictingRunId}'."),
            RepositoryFileActionResult.Rejected =>
                throw new McpException($"path '{path}' is invalid or empty - to delete an entire repository, use delete_repository instead."),
            _ => throw new McpException("Unexpected result deleting the path."),
        };
    }
}

/// <summary>One entry found by <c>find_files</c>.</summary>
/// <param name="RelativePath">The entry's path relative to the repository root.</param>
/// <param name="IsDirectory">Whether this entry is a directory.</param>
/// <param name="SizeBytes">The file's size in bytes; <see langword="null"/> for a directory.</param>
/// <param name="LastModifiedUtc">When the entry was last modified.</param>
public sealed record McpRepositoryEntry(string RelativePath, bool IsDirectory, long? SizeBytes, DateTimeOffset LastModifiedUtc);

/// <summary>The result of a <c>find_files</c> call.</summary>
/// <param name="Entries">Every matched file/directory, up to the configured cap.</param>
/// <param name="Truncated">Whether more entries matched than the configured cap allowed returning - narrow the pattern if so.</param>
public sealed record McpFindFilesResult(IReadOnlyList<McpRepositoryEntry> Entries, bool Truncated);

/// <summary>The result of a <c>get_file_info</c> call.</summary>
/// <param name="RelativePath">The entry's path relative to the repository root.</param>
/// <param name="IsDirectory">Whether this entry is a directory.</param>
/// <param name="SizeBytes">The file's size in bytes; <see langword="null"/> for a directory.</param>
/// <param name="Attributes">The entry's Windows file attribute flags, as their names (e.g. "Archive, ReadOnly").</param>
/// <param name="CreatedUtc">When the entry was created.</param>
/// <param name="LastModifiedUtc">When the entry was last modified.</param>
/// <param name="LastAccessedUtc">When the entry was last accessed.</param>
/// <param name="Owner">The entry's owning account; <see langword="null"/> if it couldn't be read.</param>
/// <param name="IsLocked">Whether another process currently has this file open; <see langword="null"/> for a directory.</param>
public sealed record McpFileInfoResult(
    string RelativePath,
    bool IsDirectory,
    long? SizeBytes,
    string Attributes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    DateTimeOffset LastAccessedUtc,
    string? Owner,
    bool? IsLocked);

/// <summary>The result of a successful <c>delete_path</c> call.</summary>
/// <param name="Deleted">Always <see langword="true"/> - a failure throws instead of returning a result with this <see langword="false"/>.</param>
public sealed record McpDeletePathResult(bool Deleted);
