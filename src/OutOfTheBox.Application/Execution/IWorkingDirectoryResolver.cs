// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Execution;

/// <summary>
/// Resolves a caller-supplied, repo-relative working directory against the configured root,
/// rejecting anything that resolves outside it (per specs/dotnet-command-execution's
/// "Working directory is confined to a configured root" requirement).
/// </summary>
public interface IWorkingDirectoryResolver
{
    /// <summary>Resolves <paramref name="relativeWorkingDirectory"/> against the configured root.</summary>
    WorkingDirectoryResolution Resolve(string relativeWorkingDirectory);

    /// <summary>
    /// Resolves <paramref name="relativePath"/> against an arbitrary already-resolved
    /// <paramref name="root"/> - the same canonicalization/symlink-resolution/containment logic
    /// <see cref="Resolve"/> uses against the configured root, reused for
    /// specs/artifact-transfer's second, narrower confinement level (a file path confined to one
    /// specific repo directory, not just the service-wide root).
    /// </summary>
    WorkingDirectoryResolution ResolveWithinRoot(string root, string relativePath);
}
