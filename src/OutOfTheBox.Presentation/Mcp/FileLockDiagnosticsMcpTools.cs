// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Diagnostics;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// The <c>get_file_lock_info</c> MCP tool, per <c>mcp-file-lock-diagnostics</c>'s spec: identifies
/// which process(es) currently have a specific file open, to diagnose a Windows "file in use"
/// build/test failure - a pitfall with no Linux analogue the sbx-side caller would already know to
/// suspect. Applies the same two-level path confinement (root→repository, then repository→file)
/// <see cref="FileTransferMcpTools.TransferFileAsync"/> already established.
/// </summary>
[McpServerToolType]
public sealed class FileLockDiagnosticsMcpTools(IWorkingDirectoryResolver workingDirectoryResolver, IFileLockInspector fileLockInspector, IMcpPermissionStore permissionStore)
{
    /// <summary>Returns which process(es) currently have a file open, from within a specific repository on the host.</summary>
    /// <param name="repository">The repository name (as returned by <c>list_repositories</c>).</param>
    /// <param name="path">The file's path, relative to the named repository.</param>
    [McpServerTool]
    [Description("Returns which process(es) currently have a file open (process id, application name, restartability) - use this to diagnose a Windows \"file in use\" build/test failure. Rejects a path that escapes the named repository or a file that doesn't exist. An empty result means the file exists and is not locked by anything.")]
    public async Task<McpFileLockInfoResult> GetFileLockInfoAsync(
        [Description("The repository name (as returned by list_repositories).")] string repository,
        [Description("The file's path, relative to the named repository.")] string path)
    {
        if (!permissionStore.IsEnabled("get_file_lock_info"))
        {
            throw new McpException("The 'get_file_lock_info' tool is currently disabled in MCP Settings.");
        }

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

        var fileResolution = workingDirectoryResolver.ResolveWithinRoot(repositoryRoot, path);
        if (!fileResolution.IsAllowed)
        {
            throw new McpException($"path '{path}' escapes repository '{repository}'.");
        }

        var filePath = fileResolution.ResolvedPath!;
        if (!File.Exists(filePath))
        {
            throw new McpException($"File '{path}' does not exist in repository '{repository}'.");
        }

        var lockingProcesses = await fileLockInspector.GetLockingProcessesAsync(filePath, CancellationToken.None);

        return new McpFileLockInfoResult(
            [.. lockingProcesses.Select(p => new McpFileLockingProcess(p.ProcessId, p.ApplicationName, p.ApplicationType.ToString(), p.IsRestartable))]);
    }
}

/// <summary>One process holding a file open.</summary>
/// <param name="ProcessId">The locking process's id.</param>
/// <param name="ApplicationName">The application name reported for this process.</param>
/// <param name="ApplicationType">The kind of application this process is classified as (e.g. "MainWindow", "Service", "Console").</param>
/// <param name="IsRestartable">Whether this process is considered safe to restart automatically.</param>
public sealed record McpFileLockingProcess(int ProcessId, string ApplicationName, string ApplicationType, bool IsRestartable);

/// <summary>The result of a <c>get_file_lock_info</c> call.</summary>
/// <param name="LockingProcesses">Every process currently holding the file open - empty if the file is not locked by anything.</param>
public sealed record McpFileLockInfoResult(IReadOnlyList<McpFileLockingProcess> LockingProcesses);
