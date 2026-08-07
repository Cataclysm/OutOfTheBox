// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.Presentation.Execution;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

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

// Kestrel/HTTPS hardening deferred to Section 16 (Transport & Network).

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

app.Run();

/// <summary>
/// Marker partial class merged with the compiler-generated top-level-statements entry point,
/// so <c>WebApplicationFactory&lt;Program&gt;</c> in another assembly (the behavior test project)
/// can reference it.
/// </summary>
public partial class Program;
