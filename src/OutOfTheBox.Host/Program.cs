// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Host.ServiceRegistration;
using OutOfTheBox.Host.Startup;
using Microsoft.AspNetCore.DataProtection;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

var dataDirectory = builder.AddOutOfTheBoxDataDirectory();
builder.AddOutOfTheBoxLogging(dataDirectory);
builder.RequireHttpsKestrelEndpoints();

// Without this, ASP.NET Core's Data Protection key ring - which both the auth cookie
// (DashboardServiceCollectionExtensions' AddCookie) and antiforgery tokens are encrypted with -
// falls back to an implicit default persistence location based on the current user profile. This
// service runs under a dedicated least-privilege account (svc-outofthebox) that may not have a
// loaded interactive profile - confirmed live, not just theorized: a real deployment logged
// "AntiforgeryValidationException: The antiforgery token could not be decrypted... key ... was not
// found in the key ring," meaning the key ring is not reliably persisting/surviving across
// restarts under that account. Since the same key ring also protects the auth cookie, the
// unfixed behavior would silently log out every operator on every service restart, not just break
// the occasional form post. Persisting explicitly to the same already-established, definitely-
// writable data directory used for the SQLite file/certificates removes the dependency on any
// profile-scoped default location entirely. SetApplicationName pins key isolation to a stable,
// explicit value rather than one implicitly derived from the content root path.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataDirectory))
    .SetApplicationName("OutOfTheBox");

builder.Services
    .AddOptions<ServiceOptions>()
    .Bind(builder.Configuration.GetSection(ServiceOptions.SectionName));

// Each of these (in ServiceRegistration/) registers one cohesive feature area's services - Host (via
// these extension methods) is the one place allowed to reference both Infrastructure and
// Presentation; neither references the other directly. See each method's own remarks for the
// lifetime rationale (singleton vs. scoped) behind its specific registrations.
builder.Services.AddCommandExecutionServices();
builder.Services.AddPersistenceServices();
builder.Services.AddRepositoryManagementServices();
builder.Services.AddResourceMonitoringServices();
builder.Services.AddDashboardServices();

// Wrapped so a fatal startup failure (a failed migration, a DI resolution error, ...) is captured
// in the log file before the process exits - not just left to whatever ephemeral console window or
// Windows Service crash dialog would otherwise be the only trace of it. Log.Fatal, not
// logger.LogCritical from an injected ILogger<T>, since a failure this early may have happened
// before or during builder.Build() itself, i.e. before DI has anything to inject - AddOutOfTheBoxLogging
// already set the static Serilog.Log.Logger as a side effect above, so it's available regardless.
try
{
    var app = builder.Build();

    await app.MigrateDatabaseAndReconcileInterruptedRunsAsync();
    await app.LoadMcpPermissionsAsync();

    app.UseOutOfTheBoxPipeline();
    app.MapOutOfTheBoxEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "OutOfTheBox terminated unexpectedly during startup.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Marker partial class merged with the compiler-generated top-level-statements entry point,
/// so <c>WebApplicationFactory&lt;Program&gt;</c> in another assembly (the behavior test project)
/// can reference it.
/// </summary>
public partial class Program;
