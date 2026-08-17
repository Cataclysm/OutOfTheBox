// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace OutOfTheBox.Host.Startup;

/// <summary>Brings the SQLite database up to date before the app starts accepting requests.</summary>
public static class DatabaseWebApplicationExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations, enables WAL mode, and reconciles any run left recorded as
    /// <see cref="RunOutcome.Running"/> by a prior process that didn't shut down cleanly.
    /// </summary>
    /// <remarks>
    /// Must run before the app starts accepting requests - a request handled before the schema exists
    /// (or before a <see cref="RunOutcome.Running"/> row from a prior process is relabeled
    /// <see cref="RunOutcome.Interrupted"/>) would see an inconsistent database.
    /// </remarks>
    public static async Task MigrateDatabaseAndReconcileInterruptedRunsAsync(this WebApplication app)
    {
        using var startupScope = app.Services.CreateScope();

        var dbContext = startupScope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();
        dbContext.Database.Migrate();
        dbContext.Database.ExecuteSql($"PRAGMA journal_mode=WAL;");

        var runRepository = startupScope.ServiceProvider.GetRequiredService<IRunRepository>();
        await runRepository.ReconcileInterruptedAsync(CancellationToken.None);
    }

    /// <summary>
    /// Loads <see cref="IMcpPermissionStore"/>'s in-memory cache from the database, seeding any
    /// missing row with its catalog default - see <see cref="IMcpPermissionStore.LoadAsync"/>'s own
    /// remarks. Must run after <see cref="MigrateDatabaseAndReconcileInterruptedRunsAsync"/> (the
    /// <c>McpToolPermissions</c> table must already exist) and before the app starts accepting
    /// requests - an MCP tool call handled before this loads would see every key as disabled
    /// (<see cref="IMcpPermissionStore.IsEnabled"/> reports <see langword="false"/> for an unknown key).
    /// </summary>
    public static async Task LoadMcpPermissionsAsync(this WebApplication app)
    {
        using var startupScope = app.Services.CreateScope();
        var permissionStore = startupScope.ServiceProvider.GetRequiredService<IMcpPermissionStore>();
        await permissionStore.LoadAsync(CancellationToken.None);
    }
}
