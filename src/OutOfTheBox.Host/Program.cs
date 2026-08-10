// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Host.ServiceRegistration;
using OutOfTheBox.Host.Startup;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

var dataDirectory = builder.AddOutOfTheBoxDataDirectory();
builder.AddOutOfTheBoxLogging(dataDirectory);
builder.RequireHttpsKestrelEndpoints();

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
