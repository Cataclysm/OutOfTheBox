// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Runtime.CompilerServices;

namespace OutOfTheBox.BehaviorTests.Support;

/// <summary>
/// Points <c>OUTOFTHEBOX_DATA_DIR</c> at a temp directory before any test runs, so Program.cs's
/// Serilog file sink (and its optional data-directory <c>appsettings.json</c> overlay) never touch
/// the real per-machine <c>%ProgramData%\OutOfTheBox</c> during an automated test run. Set exactly
/// once, at assembly load - not per <see cref="CommandExecutionServiceFactory"/> instance - because
/// <c>Environment.GetEnvironmentVariable</c> is raw process-wide mutable state (Program.cs reads it
/// before <c>IConfiguration</c> exists, so <c>ConfigureAppConfiguration</c> can't override it the
/// way every other test setting is overridden); setting it once before any factory - including two
/// running concurrently, which this test suite does for real - is what avoids a
/// last-writer-wins race. The directory is shared across every factory in the run: only the SQLite
/// file needs per-factory uniqueness (to avoid two factories contending for the same database), and
/// Serilog's file sink already supports multiple writers safely.
/// </summary>
internal static class TestDataDirectorySetup
{
    [ModuleInitializer]
    internal static void SetDataDirectoryEnvironmentVariable() =>
        Environment.SetEnvironmentVariable(
            "OUTOFTHEBOX_DATA_DIR",
            Path.Combine(Path.GetTempPath(), $"OutOfTheBox-BehaviorTests-Data-{Guid.NewGuid():N}"));
}
