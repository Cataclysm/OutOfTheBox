// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using Serilog;
using Serilog.Events;

namespace OutOfTheBox.Host.Startup;

/// <summary>Wires in Serilog file+console logging (Section 25).</summary>
public static class LoggingWebApplicationBuilderExtensions
{
    /// <summary>Adds Serilog, logging to both the console and a rolling file under <paramref name="dataDirectory"/>.</summary>
    public static void AddOutOfTheBoxLogging(this WebApplicationBuilder builder, string dataDirectory)
    {
        // The log file lives in the data directory (same folder as the SQLite file), which the
        // installer already grants the service account full control over, so no new permissioning is
        // needed. Global minimum is Warning - deliberately coarser than the framework's own default
        // Information level, so EF Core's per-query SQL logging and Kestrel's per-connection chatter
        // never reach the file (per direct instruction: "not gigabytes of logs, just the stuff that
        // really matters"). Two narrow overrides restore Information for this app's own code
        // (OutOfTheBox.*, where every remaining Information-level call site is a low-frequency
        // lifecycle event, not a per-request one) and for Microsoft.Hosting.Lifetime specifically (the
        // framework's own "Now listening on.../Application started/Application stopping" messages -
        // cheap, and valuable for correlating "when did this happen" in a bug report). Errors from
        // *any* category (including framework ones, e.g. an unhandled request exception ASP.NET
        // Core's own diagnostics middleware logs at Error) still pass the global Warning floor without
        // needing an explicit override.
        builder.Host.UseSerilog((_, _, configuration) => configuration
            .MinimumLevel.Warning()
            .MinimumLevel.Override("OutOfTheBox", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(dataDirectory, "logs", "outofthebox-.log"),
                rollingInterval: RollingInterval.Day,
                // Belt-and-suspenders volume cap on top of the Warning floor above: a 10MB/file cap
                // plus rolling onto a new file past that limit, and only the most recent 14 files
                // kept, bounds total disk usage even if something logs far more than expected on a
                // given day.
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 14,
                shared: true));
    }
}
