// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Lists, clones, and deletes repositories on the operator's behalf - called directly from Blazor
/// component code-behind, never through an HTTP endpoint (per specs/repository-management's
/// "reachable only from the authenticated dashboard" requirement; see design.md's "Repository
/// management" decision, the same in-process pattern as the resource-monitoring kill action).
/// </summary>
public interface IRepositoryManager
{
    /// <summary>Lists every repository under the configured root with its current stats and active state.</summary>
    Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts cloning <paramref name="url"/> into a new repository named <paramref name="name"/>.
    /// Returns as soon as the clone is accepted and started - the clone itself keeps running in the
    /// background, visible in Status/History via the same run id, per specs/service-dashboard's
    /// "the clone starts, appears as an in-flight run" requirement.
    /// </summary>
    Task<RepositoryActionResult> CloneAsync(string url, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the repository named <paramref name="name"/>, recursively and permanently. Unlike
    /// <see cref="CloneAsync"/>, this runs to completion before returning - a directory delete has
    /// no incremental progress worth streaming (per design.md's "Repository delete" decision).
    /// </summary>
    Task<RepositoryActionResult> DeleteAsync(string name, CancellationToken cancellationToken);
}
