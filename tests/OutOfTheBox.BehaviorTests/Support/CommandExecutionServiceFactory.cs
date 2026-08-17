// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Globalization;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Domain.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OutOfTheBox.BehaviorTests.Support;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> pointed at the checked-in fixture repositories
/// under <c>tests/Fixtures</c>, with a known test bearer token, for behavior scenarios that need
/// a real running instance of the service (real ASP.NET Core pipeline, real `dotnet.exe` child
/// processes) without a real deployed Windows Service or TCP/TLS listener.
/// </summary>
public sealed class CommandExecutionServiceFactory(
    int defaultExecutionTimeoutSeconds = 600,
    int maximumExecutionTimeoutSeconds = 3600,
    string? rootDirectoryOverride = null,
    string? sqliteFilePathOverride = null,
    long? mcpMaxFileTransferBytesOverride = null,
    int? mcpMaxFindFilesResultsOverride = null)
    : WebApplicationFactory<Program>
{
    /// <summary>The bearer token configured for this test instance.</summary>
    public const string TestBearerToken = "behavior-test-bearer-token";

    // A fresh file per factory instance, not a shared path - Program.cs runs migrations and
    // reconciliation against this on startup, and two factories running concurrently (different
    // test classes/collections) must not contend for the same SQLite file. A restart-persistence
    // scenario instead passes sqliteFilePathOverride so a second factory instance points at the
    // same file the first one wrote to, simulating the service restarting against its existing
    // database rather than a fresh one.
    private readonly bool _ownsSqliteFile = sqliteFilePathOverride is null;

    /// <summary>The SQLite file path this instance is configured against - for a restart scenario's second factory.</summary>
    public string SqliteFilePath { get; } = sqliteFilePathOverride ?? Path.Combine(Path.GetTempPath(), $"OutOfTheBox-BehaviorTests-{Guid.NewGuid():N}.db");

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Most scenarios target the checked-in tests/Fixtures/ repositories; git-command-execution
        // scenarios instead point this at a freshly-generated GitFixture (see GitFixture.cs)
        // via rootDirectoryOverride, since git commands mutate a working tree and a single
        // checked-in fixture repository can't stay deterministic across runs the way a read-only
        // dotnet fixture can.
        var fixturesRoot = rootDirectoryOverride ?? FindFixturesRoot();

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OutOfTheBox:RootDirectory"] = fixturesRoot,
            ["OutOfTheBox:BearerToken"] = TestBearerToken,
            ["OutOfTheBox:DefaultExecutionTimeoutSeconds"] = defaultExecutionTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["OutOfTheBox:MaximumExecutionTimeoutSeconds"] = maximumExecutionTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["OutOfTheBox:OutputCapBytes"] = "5242880",
            ["OutOfTheBox:SqliteFilePath"] = SqliteFilePath,
            ["OutOfTheBox:McpMaxFileTransferBytes"] = (mcpMaxFileTransferBytesOverride ?? (25 * 1024 * 1024)).ToString(CultureInfo.InvariantCulture),
            ["OutOfTheBox:McpMaxFindFilesResults"] = (mcpMaxFindFilesResultsOverride ?? 2000).ToString(CultureInfo.InvariantCulture),
        }));
    }

    /// <summary>
    /// Every MCP tool/subcommand starts enabled for this factory's own instance - most BehaviorTests
    /// features exercise a specific tool's own functionality (repository management, file management,
    /// credentials, ...), not the MCP Settings permission gate itself, and shouldn't need to know or
    /// care that some tools now default to disabled for a real, freshly-installed operator. The two
    /// scenarios that *do* test the permission gate (see <c>McpCommandExecutionSteps</c>/
    /// <c>McpFileManagementSteps</c>) explicitly disable one key themselves, after this factory (and
    /// this override) has already run - narrowing this "everything on" baseline for that one scenario
    /// only, not fighting it.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var permissionStore = scope.ServiceProvider.GetRequiredService<IMcpPermissionStore>();
        foreach (var key in McpToolCatalog.AllKeys())
        {
            permissionStore.SetEnabledAsync(key, true, CancellationToken.None).GetAwaiter().GetResult();
        }

        return host;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && _ownsSqliteFile)
        {
            // WAL mode leaves -wal/-shm sidecar files alongside the main one; delete all three,
            // ignoring failures (a lingering handle from a just-stopped Kestrel instance) since
            // this is best-effort temp-file cleanup, not a correctness requirement.
            foreach (var path in new[] { SqliteFilePath, SqliteFilePath + "-wal", SqliteFilePath + "-shm" })
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Walks up from the test assembly's output directory to the repository root (identified by
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
                "Could not locate the repository root (OutOfTheBox.slnx) from the test assembly's base directory.");
        }

        return Path.Combine(directory.FullName, "tests", "Fixtures");
    }
}
