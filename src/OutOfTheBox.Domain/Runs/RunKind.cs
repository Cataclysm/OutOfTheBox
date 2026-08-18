// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Runs;

/// <summary>Which of the service's capabilities produced a given <see cref="Run"/>.</summary>
public enum RunKind
{
    /// <summary>A <c>dotnet</c> command executed via the MCP <c>dotnet_run</c> tool.</summary>
    DotnetCommand,

    /// <summary>A <c>git</c> command executed via the MCP <c>git_run</c> tool.</summary>
    GitCommand,

    /// <summary>A file transfer served via the MCP <c>transfer_file</c> tool.</summary>
    FileTransfer,

    /// <summary>A repository clone, performed from the dashboard or via the MCP <c>clone_repository</c> tool (see specs/repository-management).</summary>
    RepositoryClone,

    /// <summary>A repository deletion, performed from the dashboard or via the MCP <c>delete_repository</c> tool (see specs/repository-management).</summary>
    RepositoryDelete,

    /// <summary>A file or directory deletion within a repository, performed from the dashboard's file tree browser or via the MCP <c>delete_path</c> tool (see specs/mcp-file-management).</summary>
    RepositoryFileDelete,
}
