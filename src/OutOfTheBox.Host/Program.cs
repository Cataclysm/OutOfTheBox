// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Monitoring;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Monitoring;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.Host;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.Presentation.Dashboard.Charts;
using OutOfTheBox.Presentation.Execution;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

// Infrastructure implementations registered against Application's ports - this file (plus
// DependencyInjection-style extension methods, if it grows large enough to warrant them) is the
// one place allowed to reference both Infrastructure and Presentation; neither references the
// other directly.
builder.Services.AddSingleton<IWorkingDirectoryResolver, WorkingDirectoryResolver>();
builder.Services.AddSingleton<IProcessRunner, CliProcessRunner>();

// Process-wide in-memory state - must be a singleton, not scoped/transient, or the per-repo lock
// would be meaningless (each request would get its own empty registry).
builder.Services.AddSingleton<RunRegistry>();

// Also process-wide/in-memory, for the same reason - one bus shared by every publisher and every
// dashboard subscriber, not one per request/circuit.
builder.Services.AddSingleton<IRunEventBus, InMemoryRunEventBus>();

// Blazor Server dashboard: interactive server-side rendering over a SignalR circuit, gated by a
// cookie issued from the login page (see LoginEndpoints) - a separate credential exchange from the
// API's per-request bearer header, since a long-lived circuit can't carry an Authorization header
// per interaction, but checked against the exact same ServiceOptions.BearerToken (per design.md's
// "Dashboard auth" decision - one shared credential, not two).
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => options.LoginPath = "/login");
builder.Services.AddAuthorization();

// Resolved lazily from IOptions<ServiceOptions> (bound above) rather than read eagerly off
// builder.Configuration here - WebApplicationFactory-driven tests merge their in-memory config
// overrides during builder.Build(), so an eager read at this point would see the pre-override
// (empty) value and every SQLite connection would silently open a private, discarded, anonymous
// database instead of the configured file.
builder.Services.AddDbContext<OutOfTheBoxDbContext>((serviceProvider, options) =>
{
    var sqliteFilePath = serviceProvider.GetRequiredService<IOptions<ServiceOptions>>().Value.SqliteFilePath;
    options.UseSqlite($"Data Source={sqliteFilePath}");
});
builder.Services.AddScoped<IRunRepository, EfRunRepository>();
builder.Services.AddScoped<IRunResourceSampleRepository, EfRunResourceSampleRepository>();

// Repository management (Section 13) - list/clone/delete are dashboard-only (no REST surface),
// called directly from Blazor component code-behind. IRepositoryManager is scoped (it depends on
// the scoped IRunRepository); the cache and stats provider are process-wide singletons, the same
// reasoning as RunRegistry/IRunEventBus above.
builder.Services.AddSingleton<RepositoryStatsCache>();
builder.Services.AddSingleton<IRepositoryStatsProvider, GitRepositoryStatsProvider>();
builder.Services.AddScoped<IRepositoryManager, RepositoryManager>();
builder.Services.AddHostedService<RepositoryStatsSampler>();

// Host/process resource monitoring (Section 14). IClock/ResourceHistoryBuffer/IResourceEventBus
// are process-wide singletons for the same reasons as RunRegistry above; IResourceSampler and
// IProcessMonitor are singletons too - both are stateless/cheap-per-call aside from the sampler's
// own internal delta-tracking state, which must persist across ticks.
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ResourceHistoryBuffer>();
builder.Services.AddSingleton<IResourceEventBus, InMemoryResourceEventBus>();
builder.Services.AddSingleton<IResourceSampler, HostResourceSampler>();
builder.Services.AddSingleton<IProcessMonitor, ProcessMonitor>();
builder.Services.AddHostedService<HostResourceSamplerService>();

// Performance graphs (Section 15). Scoped, not singleton, since it wraps IJSRuntime - a Blazor
// Server circuit-scoped service itself, so a chart-interop instance must live no longer than the
// circuit that created it.
builder.Services.AddScoped<IChartInterop, ChartInterop>();

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

app.MapCommandExecutionEndpoints();
app.MapArtifactTransferEndpoints();
app.MapLoginEndpoints();
app.MapVersionEndpoint();

// RequireAuthorization() applies only to this Razor Components route group - the four
// bearer-token-protected API endpoints above (and /login, /logout, /version) have no
// authorization metadata attached at all, so they're completely unaffected; the dashboard's own
// Login page opts back out via its [AllowAnonymous] attribute.
//
// AddAdditionalAssemblies is required now that App lives in Host rather than Presentation (moved
// so its @Assets[...] references resolve against the actual hosting app's manifest, per Section
// 15's Chart.js work) - MapRazorComponents<App>() only scans App's own assembly for @page
// components by default, and every routable page (Status, Repos, History, Login, ...) still lives
// in Presentation.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Status).Assembly)
    .RequireAuthorization();

app.Run();

/// <summary>
/// Marker partial class merged with the compiler-generated top-level-statements entry point,
/// so <c>WebApplicationFactory&lt;Program&gt;</c> in another assembly (the behavior test project)
/// can reference it.
/// </summary>
public partial class Program;
