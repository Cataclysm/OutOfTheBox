using BuildAndTestService.Host;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Services
    .AddOptions<ServiceOptions>()
    .Bind(builder.Configuration.GetSection(ServiceOptions.SectionName));

// Infrastructure DI registrations, Presentation endpoint/component mapping, and Kestrel/HTTPS
// hardening are added here as those pieces land in later implementation steps. Until then this
// is an intentionally minimal, buildable composition root.

var app = builder.Build();

app.Run();

/// <summary>
/// Marker partial class merged with the compiler-generated top-level-statements entry point,
/// so <c>WebApplicationFactory&lt;Program&gt;</c> in another assembly (the behavior test project)
/// can reference it.
/// </summary>
public partial class Program;
