// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

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
}
