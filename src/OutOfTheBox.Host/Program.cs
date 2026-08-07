// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Presentation.Execution;
using Microsoft.Extensions.Hosting.WindowsServices;

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

// Kestrel/HTTPS hardening deferred to Section 16 (Transport & Network).

var app = builder.Build();

app.MapCommandExecutionEndpoints();
app.MapArtifactTransferEndpoints();

app.Run();

/// <summary>
/// Marker partial class merged with the compiler-generated top-level-statements entry point,
/// so <c>WebApplicationFactory&lt;Program&gt;</c> in another assembly (the behavior test project)
/// can reference it.
/// </summary>
public partial class Program;
