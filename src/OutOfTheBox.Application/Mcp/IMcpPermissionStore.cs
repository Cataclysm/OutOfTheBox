// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Mcp;

/// <summary>
/// Whether one MCP tool (or one <c>dotnet_run</c>/<c>git_run</c> subcommand) is currently enabled -
/// consulted by every <c>[McpServerTool]</c> method before it does any real work, and read/written by
/// the MCP Settings dashboard page. Backed by the <c>McpToolPermissions</c> table (durable across
/// restarts) but cached in memory for <see cref="IsEnabled"/> - an MCP tool call must never wait on a
/// database round trip just to check whether it's allowed to run at all.
/// </summary>
public interface IMcpPermissionStore
{
    /// <summary>
    /// Loads every row from the database into the in-memory cache, seeding (and persisting) a row
    /// for any key <c>OutOfTheBox.Domain.Mcp.McpToolCatalog.AllKeys</c> knows about but the database
    /// doesn't yet - the first run after this feature ships, and any future release that adds a new
    /// catalog entry, both self-heal here with no manual migration step. Must run once, at startup,
    /// before the app starts accepting requests - the same "consistent before serving" requirement
    /// <c>DatabaseWebApplicationExtensions.MigrateDatabaseAndReconcileInterruptedRunsAsync</c> already
    /// documents for run-history reconciliation.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether <paramref name="key"/> (a plain tool name, or a <c>"{executable}:{subcommand}"</c>
    /// subcommand key - see <c>OutOfTheBox.Domain.Mcp.McpToolCatalog</c>) is currently enabled.
    /// Synchronous and in-memory - never awaits a database call.
    /// </summary>
    bool IsEnabled(string key);

    /// <summary>Every known key (per <c>McpToolCatalog.AllKeys</c>) and its current enabled state, for the MCP Settings page to render.</summary>
    Task<IReadOnlyDictionary<string, bool>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Persists <paramref name="enabled"/> for <paramref name="key"/> and updates the in-memory cache before returning, so the very next MCP call already sees it.</summary>
    Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken);
}
