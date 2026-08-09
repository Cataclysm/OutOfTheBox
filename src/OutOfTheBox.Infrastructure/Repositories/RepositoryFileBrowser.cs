// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.PathConfinement;
using OutOfTheBox.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IRepositoryFileBrowser" />
public sealed class RepositoryFileBrowser(IWorkingDirectoryResolver workingDirectoryResolver, RunRegistry runRegistry, ILogger<RepositoryFileBrowser> logger) : IRepositoryFileBrowser
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
    public Task<RepositoryFileActionResult> DeleteAsync(string repositoryName, string relativePath, CancellationToken cancellationToken) =>
        Task.FromResult(DeleteCore(repositoryName, relativePath));

    private RepositoryFileActionResult DeleteCore(string repositoryName, string relativePath)
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

        try
        {
            if (isDirectory)
            {
                ClearReadOnlyAttributes(resolvedPath);
                Directory.Delete(resolvedPath, recursive: true);
            }
            else
            {
                File.SetAttributes(resolvedPath, FileAttributes.Normal);
                File.Delete(resolvedPath);
            }

            return new RepositoryFileActionResult.Succeeded();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Failed to delete {ResolvedPath} in repository '{RepositoryName}'.", resolvedPath, repositoryName);
            return new RepositoryFileActionResult.Failed(ex.Message);
        }
        finally
        {
            runRegistry.Release(repositoryRoot);
        }
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

    /// <summary>Same read-only-clearing workaround <see cref="RepositoryManager"/>'s own delete uses - a git checkout can leave pack/object-adjacent files read-only, which <see cref="Directory.Delete(string, bool)"/> otherwise throws on.</summary>
    private static void ClearReadOnlyAttributes(string path)
    {
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
    }
}
