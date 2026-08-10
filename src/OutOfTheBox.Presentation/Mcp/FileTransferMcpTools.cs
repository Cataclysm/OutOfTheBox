// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// MCP <c>transfer_file</c> tool, per <c>mcp-file-transfer</c>'s spec: retrieves a single file's
/// contents from within one specific repository's directory tree, applying the same two-level path
/// confinement (root→repository, then repository→file) every other repository-relative-path tool in
/// this service uses. A single synchronous request/response - no polling, no background
/// continuation, no cancellation - since the configured size cap
/// (<see cref="ServiceOptions.McpMaxFileTransferBytes"/>) keeps a transfer small enough to always
/// complete within one tool call; a streamed, cancellable, potentially-long-running transfer has no
/// MCP equivalent worth building here.
/// </summary>
[McpServerToolType]
public sealed class FileTransferMcpTools(
    IWorkingDirectoryResolver workingDirectoryResolver,
    IRunRepository runRepository,
    IRunEventBus runEventBus,
    IOptions<ServiceOptions> options,
    ILogger<FileTransferMcpTools> logger)
{
    /// <summary>Returns a file's contents (base64-encoded) from within a specific repository on the host.</summary>
    /// <param name="repository">The repository name (as returned by <c>list_repositories</c>).</param>
    /// <param name="path">The file's path, relative to the named repository.</param>
    [McpServerTool]
    [Description("Returns a file's contents (base64-encoded) from within a specific repository on the host. Rejects a path that escapes the named repository, a file that doesn't exist, or one larger than the configured limit.")]
    public async Task<McpTransferFileResult> TransferFileAsync(
        [Description("The repository name (as returned by list_repositories).")] string repository,
        [Description("The file's path, relative to the named repository.")] string path)
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

        var fileResolution = workingDirectoryResolver.ResolveWithinRoot(repositoryRoot, path);
        if (!fileResolution.IsAllowed)
        {
            throw new McpException($"path '{path}' escapes repository '{repository}'.");
        }

        var filePath = fileResolution.ResolvedPath!;
        var runId = Guid.NewGuid();

        if (!File.Exists(filePath))
        {
            await RecordAsync(runId, repositoryRoot, path, RunOutcome.NotFound, null);
            throw new McpException($"File '{path}' does not exist in repository '{repository}'.");
        }

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > options.Value.McpMaxFileTransferBytes)
        {
            await RecordAsync(runId, repositoryRoot, path, RunOutcome.Failed, null);
            throw new McpException(
                $"File '{path}' is {fileInfo.Length} bytes, exceeding the configured limit of {options.Value.McpMaxFileTransferBytes} bytes.");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, CancellationToken.None);
            await RecordAsync(runId, repositoryRoot, path, RunOutcome.Completed, bytes.LongLength);
            return new McpTransferFileResult(runId, Convert.ToBase64String(bytes), bytes.LongLength);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file exists (already checked above) but can't actually be read - locked by another
            // process, or a permission problem.
            await RecordAsync(runId, repositoryRoot, path, RunOutcome.Failed, null);
            logger.LogError(ex, "Failed to read file for MCP transfer: {FilePath} (run {RunId}).", filePath, runId);
            throw new McpException($"Failed to read file '{path}': {ex.Message}");
        }
    }

    private async Task RecordAsync(Guid runId, string repositoryRoot, string path, RunOutcome outcome, long? fileSizeBytes)
    {
        var now = DateTimeOffset.UtcNow;
        var run = new Run
        {
            Id = runId,
            Kind = RunKind.FileTransfer,
            RepositoryPath = repositoryRoot,
            FilePath = path,
            FileSizeBytes = fileSizeBytes,
            StartedAt = now,
            CompletedAt = now,
            Outcome = outcome,
        };
        await runRepository.AddAsync(run, CancellationToken.None);
        runEventBus.Publish(new RunEvent(runId, RunKind.FileTransfer, RunEventType.Terminal, repositoryRoot));
    }
}

/// <summary>The result of a successful <c>transfer_file</c> call.</summary>
/// <param name="RunId">This transfer's id, as recorded in run history - not otherwise needed to use the result, but useful for correlation/debugging, the same way every other MCP tool that touches run history surfaces its run id.</param>
/// <param name="ContentBase64">The file's contents, base64-encoded.</param>
/// <param name="SizeBytes">The file's size in bytes (of the raw, non-base64-encoded content).</param>
public sealed record McpTransferFileResult(Guid RunId, string ContentBase64, long SizeBytes);
