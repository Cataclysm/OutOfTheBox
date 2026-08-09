// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Domain.PathConfinement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Execution;

/// <summary>
/// Resolves a caller-supplied working directory against <see cref="ServiceOptions.RootDirectory"/>
/// using lexical canonicalization (<see cref="Path.GetFullPath(string)"/>) plus symlink/junction
/// resolution, then defers the actual containment decision to
/// <see cref="PathConfinementPolicy"/>.
/// </summary>
public sealed class WorkingDirectoryResolver(IOptions<ServiceOptions> options, ILogger<WorkingDirectoryResolver> logger) : IWorkingDirectoryResolver
{
    /// <inheritdoc />
    public WorkingDirectoryResolution Resolve(string relativeWorkingDirectory) =>
        ResolveWithinRoot(options.Value.RootDirectory, relativeWorkingDirectory);

    /// <inheritdoc />
    public WorkingDirectoryResolution ResolveWithinRoot(string root, string relativePath)
    {
        var rootFullPath = Path.GetFullPath(root);

        string combined;
        try
        {
            // Path.Combine discards the root entirely if relativePath is itself rooted/absolute
            // (e.g. "C:\Windows"), so an absolute-path escape attempt still ends up correctly
            // rejected by the containment check below rather than needing special-casing.
            combined = Path.GetFullPath(Path.Combine(rootFullPath, relativePath));
        }
        catch (ArgumentException)
        {
            return WorkingDirectoryResolution.Rejected();
        }

        var finalPath = ResolveSymlinkTarget(combined);

        return PathConfinementPolicy.IsContained(rootFullPath, finalPath)
            ? WorkingDirectoryResolution.Allowed(finalPath)
            : WorkingDirectoryResolution.Rejected();
    }

    /// <summary>
    /// If <paramref name="path"/> is itself a symbolic link or junction, follows it to its final
    /// target so the containment check runs against where the path actually leads, not just its
    /// lexical text. Non-links (including paths that don't exist yet) pass through unchanged.
    /// </summary>
    private string ResolveSymlinkTarget(string path)
    {
        try
        {
            var finalTarget = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            return finalTarget?.FullName ?? path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A genuinely broken/circular link or an access-denied reading the reparse point (not
            // "doesn't exist yet," which returns null above without throwing) - worth a trace since
            // the containment check below silently falls back to a lexical-only comparison against
            // this path instead of its true resolved target, which is a security-relevant behavior
            // change for this specific request. Rare enough in practice (most working directories
            // aren't links at all) not to be noise despite this running on every request.
            logger.LogWarning(ex, "Failed to resolve symlink/junction target for {Path}; falling back to the unresolved path for confinement.", path);
            return path;
        }
    }
}
