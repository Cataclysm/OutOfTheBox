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
}
