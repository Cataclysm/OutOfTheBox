using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BuildAndTestService.BehaviorTests.Support;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> pointed at the checked-in fixture repos
/// under <c>tests/Fixtures</c>, with a known test bearer token, for behavior scenarios that need
/// a real running instance of the service (real ASP.NET Core pipeline, real `dotnet.exe` child
/// processes) without a real deployed Windows Service or TCP/TLS listener.
/// </summary>
public sealed class CommandExecutionServiceFactory(int defaultExecutionTimeoutSeconds = 600, int maximumExecutionTimeoutSeconds = 3600)
    : WebApplicationFactory<Program>
{
    /// <summary>The bearer token configured for this test instance.</summary>
    public const string TestBearerToken = "behavior-test-bearer-token";

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var fixturesRoot = FindFixturesRoot();

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BuildAndTestService:RootDirectory"] = fixturesRoot,
                ["BuildAndTestService:BearerToken"] = TestBearerToken,
                ["BuildAndTestService:DefaultExecutionTimeoutSeconds"] = defaultExecutionTimeoutSeconds.ToString(),
                ["BuildAndTestService:MaximumExecutionTimeoutSeconds"] = maximumExecutionTimeoutSeconds.ToString(),
                ["BuildAndTestService:OutputCapBytes"] = "5242880",
            });
        });
    }

    /// <summary>
    /// Walks up from the test assembly's output directory to the repo root (identified by
    /// <c>BuildAndTestService.slnx</c>), then down into <c>tests/Fixtures</c> - avoids hardcoding
    /// a machine-specific absolute path.
    /// </summary>
    private static string FindFixturesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BuildAndTestService.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repo root (BuildAndTestService.slnx) from the test assembly's base directory.");
        }

        return Path.Combine(directory.FullName, "tests", "Fixtures");
    }
}
