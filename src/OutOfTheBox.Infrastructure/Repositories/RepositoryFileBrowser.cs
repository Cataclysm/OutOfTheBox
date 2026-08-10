// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Security.AccessControl;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Diagnostics;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.PathConfinement;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IRepositoryFileBrowser" />
public sealed class RepositoryFileBrowser(
    IWorkingDirectoryResolver workingDirectoryResolver,
    RunRegistry runRegistry,
    IRunRepository runRepository,
    IRunEventBus runEventBus,
    IFileLockInspector fileLockInspector,
    IOptions<ServiceOptions> options,
    ILogger<RepositoryFileBrowser> logger) : IRepositoryFileBrowser
{
    /// <inheritdoc />
    public Task<IReadOnlyList<RepositoryFileEntry>> ListDirectoryAsync(string repositoryName, string relativePath, CancellationToken cancellationToken)
    {
        var resolvedDirectory = ResolveWithinRepository(repositoryName, relativePath);
        if (resolvedDirectory is null || !Directory.Exists(resolvedDirectory))
        {
            return Task.FromResult<IReadOnlyList<RepositoryFileEntry>>([]);
        }

        var entries = new List<RepositoryFileEntry>();

        try
        {
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(resolvedDirectory))
            {
                try
                {
                    if (Directory.Exists(entryPath))
                    {
                        var directoryInfo = new DirectoryInfo(entryPath);
                        entries.Add(new RepositoryFileEntry(directoryInfo.Name, IsDirectory: true, SizeBytes: null, directoryInfo.LastWriteTimeUtc));
                    }
                    else
                    {
                        var fileInfo = new FileInfo(entryPath);
                        entries.Add(new RepositoryFileEntry(fileInfo.Name, IsDirectory: false, fileInfo.Length, fileInfo.LastWriteTimeUtc));
                    }
                }
                catch (IOException)
                {
                    // Deleted/moved mid-enumeration (e.g. a build running concurrently) - skip it
                    // rather than fail the whole listing for one transient entry. Not logged - this
                    // is routine and could repeat once per entry in a directory undergoing heavy
                    // concurrent writes, exactly the per-request log noise this file's other new
                    // logging deliberately avoids.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unlike a single transient entry above, the whole listing failing outright (e.g. the
            // folder itself denies access) means the operator sees an empty folder with no
            // indication anything went wrong - worth a trace, and low-frequency enough (once per
            // failed listing, not once per entry) not to be noise.
            logger.LogWarning(ex, "Failed to list directory {ResolvedDirectory} for repository '{RepositoryName}'.", resolvedDirectory, repositoryName);
        }

        // Folders first, then alphabetical within each group - the conventional Explorer-style sort.
        return Task.FromResult<IReadOnlyList<RepositoryFileEntry>>(
            [.. entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <inheritdoc />
    public Task<RepositoryEntrySearchResult> FindEntriesAsync(string repositoryName, string pattern, CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(repositoryName);
        if (repositoryRoot is null || !Directory.Exists(repositoryRoot))
        {
            return Task.FromResult(new RepositoryEntrySearchResult([], Truncated: false));
        }

        var matcher = new Matcher();
        matcher.AddInclude(string.IsNullOrWhiteSpace(pattern) ? "**/*" : pattern);

        var matches = new List<RepositoryEntryMatch>();
        var truncated = false;
        var cap = options.Value.McpMaxFindFilesResults;

        try
        {
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(repositoryRoot, "*", SearchOption.AllDirectories))
            {
                if (matches.Count >= cap)
                {
                    truncated = true;
                    break;
                }

                try
                {
                    // Matcher.Match tests a plain relative-path string against the configured
                    // patterns - it has no filesystem opinions of its own, so it works identically
                    // for a file's or a directory's relative path (see design.md's "Glob matching"
                    // decision for why this - not Matcher's own file-only Execute - is used here).
                    var relativePath = Path.GetRelativePath(repositoryRoot, entryPath).Replace('\\', '/');
                    if (!matcher.Match(relativePath).HasMatches)
                    {
                        continue;
                    }

                    if (Directory.Exists(entryPath))
                    {
                        var directoryInfo = new DirectoryInfo(entryPath);
                        matches.Add(new RepositoryEntryMatch(relativePath, directoryInfo.Name, IsDirectory: true, SizeBytes: null, directoryInfo.LastWriteTimeUtc));
                    }
                    else
                    {
                        var fileInfo = new FileInfo(entryPath);
                        matches.Add(new RepositoryEntryMatch(relativePath, fileInfo.Name, IsDirectory: false, fileInfo.Length, fileInfo.LastWriteTimeUtc));
                    }
                }
                catch (IOException)
                {
                    // Same transient-entry reasoning as ListDirectoryAsync above.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to search repository '{RepositoryName}' for pattern '{Pattern}'.", repositoryName, pattern);
        }

        return Task.FromResult(new RepositoryEntrySearchResult(
            [.. matches.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)], truncated));
    }

    /// <inheritdoc />
    public async Task<RepositoryEntryMetadata?> GetMetadataAsync(string repositoryName, string relativePath, CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveWithinRepository(repositoryName, relativePath);
        if (resolvedPath is null)
        {
            return null;
        }

        var isDirectory = Directory.Exists(resolvedPath);
        if (!isDirectory && !File.Exists(resolvedPath))
        {
            return null;
        }

        FileSystemInfo info = isDirectory ? new DirectoryInfo(resolvedPath) : new FileInfo(resolvedPath);
        var owner = TryGetOwner(resolvedPath, isDirectory);

        // Restart Manager's own model is file-based - a directory can't itself be "locked" the way
        // an open file can, so this stays null rather than a misleading false.
        bool? isLocked = null;
        if (!isDirectory)
        {
            var lockingProcesses = await fileLockInspector.GetLockingProcessesAsync(resolvedPath, cancellationToken);
            isLocked = lockingProcesses.Count > 0;
        }

        return new RepositoryEntryMetadata(
            RelativePath: relativePath.Replace('\\', '/'),
            Name: info.Name,
            IsDirectory: isDirectory,
            SizeBytes: isDirectory ? null : ((FileInfo)info).Length,
            Attributes: info.Attributes,
            CreatedUtc: info.CreationTimeUtc,
            LastModifiedUtc: info.LastWriteTimeUtc,
            LastAccessedUtc: info.LastAccessTimeUtc,
            Owner: owner,
            IsLocked: isLocked);
    }

    // The owning account, as a friendly "DOMAIN\name" if it resolves, falling back to the raw SID if
    // not (e.g. an orphaned account no longer in Active Directory/local accounts) - either way beats
    // silently omitting a field the caller explicitly asked for. Reading the ACL itself can fail
    // independently (e.g. a permissions problem on the entry) without that being fatal to every
    // other metadata field already gathered, so this degrades to null rather than throwing.
    private static string? TryGetOwner(string path, bool isDirectory)
    {
        try
        {
            var security = isDirectory
                ? (FileSystemSecurity)new DirectoryInfo(path).GetAccessControl()
                : new FileInfo(path).GetAccessControl();
            var owner = security.GetOwner(typeof(System.Security.Principal.NTAccount))
                ?? security.GetOwner(typeof(System.Security.Principal.SecurityIdentifier));
            return owner?.Value;
        }
        catch (SystemException)
        {
            // Covers IOException/UnauthorizedAccessException/InvalidOperationException/
            // PlatformNotSupportedException - every documented GetAccessControl() failure mode.
            return null;
        }
    }

    /// <inheritdoc />
    public Task<RepositoryFileActionResult> DeleteAsync(string repositoryName, string relativePath, CancellationToken cancellationToken) =>
        DeleteCoreAsync(repositoryName, relativePath, cancellationToken);

    private async Task<RepositoryFileActionResult> DeleteCoreAsync(string repositoryName, string relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            // Removing a repository entirely is IRepositoryManager.DeleteAsync's job, not this one's.
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var repositoryRoot = ResolveRepositoryRoot(repositoryName);
        var resolvedPath = ResolveWithinRepository(repositoryName, relativePath);
        if (repositoryRoot is null || resolvedPath is null)
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var isDirectory = Directory.Exists(resolvedPath);
        if (!isDirectory && !File.Exists(resolvedPath))
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.NotFound);
        }

        var runId = Guid.NewGuid();
        using var cancelRequestCts = new CancellationTokenSource();
        if (!runRegistry.TryAcquire(repositoryRoot, runId, cancelRequestCts, out var conflictingRunId))
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
        }

        RepositoryFileActionResult result;
        var startedAt = DateTimeOffset.UtcNow;
        string? errorMessage = null;

        try
        {
            if (isDirectory)
            {
                await RecursiveDelete.DirectoryAsync(resolvedPath, cancellationToken);
            }
            else
            {
                await RecursiveDelete.FileAsync(resolvedPath, cancellationToken);
            }

            result = new RepositoryFileActionResult.Succeeded();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Failed to delete {ResolvedPath} in repository '{RepositoryName}'.", resolvedPath, repositoryName);
            errorMessage = ex.Message;
            result = new RepositoryFileActionResult.Failed(ex.Message);
        }
        finally
        {
            runRegistry.Release(repositoryRoot);
        }

        // Recorded here (not just by the MCP tool that also reaches this method) so the dashboard's
        // own file tree browser delete gets the same history trail - previously neither caller left
        // any trace at all of who deleted what and when, per design.md's "gains its own run-history
        // record" decision.
        var now = DateTimeOffset.UtcNow;
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.RepositoryFileDelete,
            RepositoryPath = repositoryRoot,
            FilePath = relativePath,
            StartedAt = startedAt,
            CompletedAt = now,
            Outcome = result is RepositoryFileActionResult.Succeeded ? RunOutcome.Completed : RunOutcome.Failed,
            Stderr = errorMessage,
        }, CancellationToken.None);
        runEventBus.Publish(new RunEvent(runId, RunKind.RepositoryFileDelete, RunEventType.Terminal, repositoryRoot));

        return result;
    }

    /// <inheritdoc />
    public Task<RepositoryFileActionResult> RenameAsync(string repositoryName, string relativePath, string newName, CancellationToken cancellationToken) =>
        Task.FromResult(RenameCore(repositoryName, relativePath, newName));

    private RepositoryFileActionResult RenameCore(string repositoryName, string relativePath, string newName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(['/', '\\']) >= 0 || newName is "." or "..")
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var repositoryRoot = ResolveRepositoryRoot(repositoryName);
        var resolvedPath = ResolveWithinRepository(repositoryName, relativePath);
        if (repositoryRoot is null || resolvedPath is null)
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var isDirectory = Directory.Exists(resolvedPath);
        if (!isDirectory && !File.Exists(resolvedPath))
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.NotFound);
        }

        var parentDirectory = Path.GetDirectoryName(resolvedPath)!;
        var destinationPath = Path.GetFullPath(Path.Combine(parentDirectory, newName));

        // newName rejecting path separators above already prevents moving elsewhere, but this is a
        // second, defense-in-depth confinement check - the same belt-and-suspenders posture
        // path-confinement uses everywhere else in this codebase.
        if (!PathConfinementPolicy.IsContained(repositoryRoot, destinationPath))
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.AlreadyExists);
        }

        var runId = Guid.NewGuid();
        using var cancelRequestCts = new CancellationTokenSource();
        if (!runRegistry.TryAcquire(repositoryRoot, runId, cancelRequestCts, out var conflictingRunId))
        {
            return new RepositoryFileActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
        }

        try
        {
            if (isDirectory)
            {
                Directory.Move(resolvedPath, destinationPath);
            }
            else
            {
                File.Move(resolvedPath, destinationPath);
            }

            return new RepositoryFileActionResult.Succeeded();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Failed to rename {ResolvedPath} to '{NewName}' in repository '{RepositoryName}'.", resolvedPath, newName, repositoryName);
            return new RepositoryFileActionResult.Failed(ex.Message);
        }
        finally
        {
            runRegistry.Release(repositoryRoot);
        }
    }

    /// <inheritdoc />
    public Task<string?> ResolveConfinedFilePathAsync(string repositoryName, string relativePath, CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveWithinRepository(repositoryName, relativePath);
        return Task.FromResult(resolvedPath is not null && File.Exists(resolvedPath) ? resolvedPath : null);
    }

    private string? ResolveRepositoryRoot(string repositoryName)
    {
        var resolution = workingDirectoryResolver.Resolve(repositoryName);
        return resolution.IsAllowed ? resolution.ResolvedPath : null;
    }

    private string? ResolveWithinRepository(string repositoryName, string relativePath)
    {
        var repositoryRoot = ResolveRepositoryRoot(repositoryName);
        if (repositoryRoot is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(relativePath))
        {
            return repositoryRoot;
        }

        var resolution = workingDirectoryResolver.ResolveWithinRoot(repositoryRoot, relativePath);
        return resolution.IsAllowed ? resolution.ResolvedPath : null;
    }
}
