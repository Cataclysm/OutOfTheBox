// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace OutOfTheBox.BehaviorTests.Support;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> pointed at the checked-in fixture repos
/// under <c>tests/Fixtures</c>, with a known test bearer token, for behavior scenarios that need
/// a real running instance of the service (real ASP.NET Core pipeline, real `dotnet.exe` child
/// processes) without a real deployed Windows Service or TCP/TLS listener.
/// </summary>
public sealed class CommandExecutionServiceFactory(
    int defaultExecutionTimeoutSeconds = 600,
    int maximumExecutionTimeoutSeconds = 3600,
    string? rootDirectoryOverride = null)
    : WebApplicationFactory<Program>
{
    /// <summary>The bearer token configured for this test instance.</summary>
    public const string TestBearerToken = "behavior-test-bearer-token";

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Most scenarios target the checked-in tests/Fixtures/ repos; git-command-execution
        // scenarios instead point this at a freshly-generated GitFixture (see GitFixture.cs)
        // via rootDirectoryOverride, since git commands mutate a working tree and a single
        // checked-in fixture repo can't stay deterministic across runs the way a read-only
        // dotnet fixture can.
        var fixturesRoot = rootDirectoryOverride ?? FindFixturesRoot();

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OutOfTheBox:RootDirectory"] = fixturesRoot,
                ["OutOfTheBox:BearerToken"] = TestBearerToken,
                ["OutOfTheBox:DefaultExecutionTimeoutSeconds"] = defaultExecutionTimeoutSeconds.ToString(),
                ["OutOfTheBox:MaximumExecutionTimeoutSeconds"] = maximumExecutionTimeoutSeconds.ToString(),
                ["OutOfTheBox:OutputCapBytes"] = "5242880",
            });
        });
    }

    /// <summary>
    /// Walks up from the test assembly's output directory to the repo root (identified by
    /// <c>OutOfTheBox.slnx</c>), then down into <c>tests/Fixtures</c> - avoids hardcoding
    /// a machine-specific absolute path. Public so steps needing the real on-disk fixture path
    /// (e.g. to compare a transferred file's bytes against the source) can reuse it.
    /// </summary>
    public static string FindFixturesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OutOfTheBox.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repo root (OutOfTheBox.slnx) from the test assembly's base directory.");
        }

        return Path.Combine(directory.FullName, "tests", "Fixtures");
    }
}
