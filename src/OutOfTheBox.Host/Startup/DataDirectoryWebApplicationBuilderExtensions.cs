// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Host.Startup;

/// <summary>Resolves and wires in this service's data directory (config + SQLite file + logs).</summary>
public static class DataDirectoryWebApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the data-directory <c>appsettings.json</c> overlay to configuration and returns the
    /// resolved data directory path, for the logging setup that also needs it.
    /// </summary>
    public static string AddOutOfTheBoxDataDirectory(this WebApplicationBuilder builder)
    {
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

        return dataDirectory;
    }
}
