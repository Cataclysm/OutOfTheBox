// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Execution;
using ModelContextProtocol;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// The two-level "resolve a repository name, then a path within it" confinement check every
/// repository-relative-path MCP tool (<c>find_files</c>, <c>get_file_info</c>, <c>delete_path</c>,
/// <c>transfer_file</c>, <c>get_file_lock_info</c>) repeats verbatim, including the exact rejection
/// message text - extracted once the same lines appeared, unchanged, in four different files.
/// </summary>
internal static class McpRepositoryPathResolution
{
    /// <summary>Resolves <paramref name="repository"/> to its absolute root path, or throws an <see cref="McpException"/> if it's invalid or outside the configured root.</summary>
    public static string ResolveRepositoryRoot(this IWorkingDirectoryResolver workingDirectoryResolver, string repository)
    {
        var resolution = workingDirectoryResolver.Resolve(repository);
        if (!resolution.IsAllowed)
        {
            throw new McpException($"repository '{repository}' is outside the configured root.");
        }

        return resolution.ResolvedPath!;
    }

    /// <summary>Resolves <paramref name="path"/> (relative to <paramref name="repositoryRoot"/>) to its absolute path, or throws an <see cref="McpException"/> naming <paramref name="repository"/> if it would escape that root.</summary>
    public static string ResolveFilePath(this IWorkingDirectoryResolver workingDirectoryResolver, string repositoryRoot, string repository, string path)
    {
        var resolution = workingDirectoryResolver.ResolveWithinRoot(repositoryRoot, path);
        if (!resolution.IsAllowed)
        {
            throw new McpException($"path '{path}' escapes repository '{repository}'.");
        }

        return resolution.ResolvedPath!;
    }
}
