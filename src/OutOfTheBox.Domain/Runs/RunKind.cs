// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

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

    /// <summary>A repository deletion performed from the dashboard only - not reachable via any MCP tool (see specs/repository-management).</summary>
    RepositoryDelete,
}
