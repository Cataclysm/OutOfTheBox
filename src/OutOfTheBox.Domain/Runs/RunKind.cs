// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Runs;

/// <summary>Which of the service's capabilities produced a given <see cref="Run"/>.</summary>
public enum RunKind
{
    /// <summary>A <c>dotnet</c> command executed via <c>POST /run</c>.</summary>
    DotnetCommand,

    /// <summary>A <c>git</c> command executed via <c>POST /run/git</c>.</summary>
    GitCommand,

    /// <summary>A file transfer served via <c>POST /files</c>.</summary>
    FileTransfer,

    /// <summary>A repository clone, performed from the dashboard or via <c>POST /repositories/clone</c> (see specs/repository-management).</summary>
    RepositoryClone,

    /// <summary>A repository deletion performed from the dashboard only - never reachable via REST (see specs/repository-management).</summary>
    RepositoryDelete,
}
