// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Host;
using OutOfTheBox.Host.ServiceRegistration;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Presentation.Authentication;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.Presentation.Execution;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using System.Security.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

// Data directory (config + SQLite file) - separate from the install directory per design.md's
// Packaging decision, so `upgrade.ps1` replacing the install directory never touches it. Defaults
// to %ProgramData%\OutOfTheBox; overridable via OUTOFTHEBOX_DATA_DIR for local dev/testing without
// touching the real machine-wide ProgramData tree. install.ps1 writes the real production
// appsettings.json here (root directory, bearer token, port, timeouts, output cap, SQLite path);
// the bundled appsettings.json next to the exe only supplies non-secret defaults, so it stays safe
// to overwrite on every upgrade. Optional (not required to exist) so `dotnet run`/BehaviorTests,
// which configure everything via environment variables instead, are unaffected.
var dataDirectory = Environment.GetEnvironmentVariable("OUTOFTHEBOX_DATA_DIR")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OutOfTheBox");
builder.Configuration.AddJsonFile(Path.Combine(dataDirectory, "appsettings.json"), optional: true, reloadOnChange: true);

// File+console logging (Section 25) - the log file lives in the data directory (same folder as the
// SQLite file), which the installer already grants the service account full control over, so no new
// permissioning is needed. Global minimum is Warning - deliberately coarser than the framework's own
// default Information level, so EF Core's per-query SQL logging and Kestrel's per-connection chatter
// never reach the file (per direct instruction: "not gigabytes of logs, just the stuff that really
// matters"). Two narrow overrides restore Information for this app's own code (OutOfTheBox.*, where
// every remaining Information-level call site is a low-frequency lifecycle event, not a per-request
// one) and for Microsoft.Hosting.Lifetime specifically (the framework's own "Now listening on.../
// Application started/Application stopping" messages - cheap, and valuable for correlating "when did
// this happen" in a bug report). Errors from *any* category (including framework ones, e.g. an
// unhandled request exception ASP.NET Core's own diagnostics middleware logs at Error) still pass
// the global Warning floor without needing an explicit override.
builder.Host.UseSerilog((_, _, configuration) => configuration
    .MinimumLevel.Warning()
    .MinimumLevel.Override("OutOfTheBox", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(dataDirectory, "logs", "outofthebox-.log"),
        rollingInterval: RollingInterval.Day,
        // Belt-and-suspenders volume cap on top of the Warning floor above: a 10MB/file cap plus
        // rolling onto a new file past that limit, and only the most recent 14 files kept, bounds
        // total disk usage even if something logs far more than expected on a given day.
        fileSizeLimitBytes: 10 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 14,
        shared: true));

// Require HTTPS on every configured Kestrel endpoint (Section 16, per design.md's Transport
// decision): the bearer token, command arguments/output, and the dashboard's cookie session all
// cross this port, so plain HTTP would leak them to anyone on-path. Fails fast at startup rather
// than silently accepting an endpoint someone configured as "http://" by mistake - there is no
// legitimate reason for this service to ever accept an unencrypted connection.
foreach (var endpoint in builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren())
{
    var url = endpoint["Url"];
    if (url is not null && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Kestrel endpoint '{endpoint.Key}' is configured as '{url}' - this service must not accept plain HTTP connections.");
    }
}

builder.WebHost.ConfigureKestrel(options =>
    options.ConfigureHttpsDefaults(https => https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13));

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
// before or during builder.Build() itself, i.e. before DI has anything to inject - UseSerilog
// already set the static Serilog.Log.Logger as a side effect above, so it's available regardless.
try
{
    var app = builder.Build();

    // Applying migrations, enabling WAL, and reconciling interrupted runs must happen before the app
    // starts accepting requests - a request handled before the schema exists (or before a `Running`
    // row from a prior process is relabeled `Interrupted`) would see an inconsistent database.
    using (var startupScope = app.Services.CreateScope())
    {
        var dbContext = startupScope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();
        dbContext.Database.Migrate();
        dbContext.Database.ExecuteSql($"PRAGMA journal_mode=WAL;");

        var runRepository = startupScope.ServiceProvider.GetRequiredService<IRunRepository>();
        await runRepository.ReconcileInterruptedAsync(CancellationToken.None);
    }

    // Serves static content (dashboard.css, the vendored Chart.js/interop script, and the
    // framework-provided blazor.web.js, all referenced via @Assets[...] in App.razor rather than
    // hardcoded paths - required for MapStaticAssets to resolve them at all, not just for
    // fingerprinted caching) before authentication - none of it needs a login, and the dashboard's
    // own login page itself needs its stylesheet before the operator has a session. Without this,
    // every static asset 404s even though the files are physically present in the build/publish
    // output - there is no other middleware in this pipeline that serves them.
    app.MapStaticAssets();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    app.MapRepositoryFileDownloadEndpoints();
    app.MapCertificateDownloadEndpoints();
    app.MapLoginEndpoints();
    app.MapVersionEndpoint();

    // MCP server (sbx-mcp-server) - the same shared bearer token service-authentication has always
    // used, applied to the MCP route via middleware (MapMcp's builder type doesn't support
    // AddEndpointFilter - see McpAuthenticationMiddleware's own remarks) so an unauthenticated
    // request is rejected before the MCP handshake, tool listing, or any tool call is processed, per
    // mcp-server's own requirement.
    app.UseMcpBearerAuthentication("/mcp");
    app.MapMcp("/mcp");

    // RequireAuthorization() applies to this Razor Components route group the same way it's applied
    // directly to MapRepositoryFileDownloadEndpoints/MapCertificateDownloadEndpoints above (both
    // cookie-authenticated download links, since they're plain browser navigations). Neither the MCP route above (its
    // own bearer-token middleware, not ASP.NET Core's cookie-based authorization) nor the dashboard's
    // own Login page (its [AllowAnonymous] attribute) are affected by this.
    //
    // AddAdditionalAssemblies is required now that App lives in Host rather than Presentation (moved
    // so its @Assets[...] references resolve against the actual hosting app's manifest, per Section
    // 15's Chart.js work) - MapRazorComponents<App>() only scans App's own assembly for @page
    // components by default, and every routable page (Status, Repositories, History, Login, ...) still lives
    // in Presentation.
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .AddAdditionalAssemblies(typeof(Status).Assembly)
        .RequireAuthorization();

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
