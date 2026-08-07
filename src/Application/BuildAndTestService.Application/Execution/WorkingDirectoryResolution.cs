// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace BuildAndTestService.Application.Execution;

/// <summary>The outcome of resolving a caller-supplied relative working directory against the configured root.</summary>
public sealed record WorkingDirectoryResolution
{
    /// <summary>Whether the resolved path falls within the configured root.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>
    /// The fully-resolved, symlink-followed absolute path, when <see cref="IsAllowed"/> is
    /// <see langword="true"/>; otherwise <see langword="null"/>.
    /// </summary>
    public string? ResolvedPath { get; init; }

    /// <summary>Creates an allowed resolution carrying the resolved path.</summary>
    public static WorkingDirectoryResolution Allowed(string resolvedPath) =>
        new() { IsAllowed = true, ResolvedPath = resolvedPath };

    /// <summary>Creates a rejected resolution.</summary>
    public static WorkingDirectoryResolution Rejected() =>
        new() { IsAllowed = false, ResolvedPath = null };
}
