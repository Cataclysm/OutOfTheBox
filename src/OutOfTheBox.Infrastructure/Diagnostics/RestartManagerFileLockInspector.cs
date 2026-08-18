// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Runtime.Versioning;
using OutOfTheBox.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Diagnostics;

/// <inheritdoc cref="IFileLockInspector" />
[SupportedOSPlatform("windows")]
public sealed class RestartManagerFileLockInspector(ILogger<RestartManagerFileLockInspector> logger) : IFileLockInspector
{
    /// <inheritdoc />
    public Task<IReadOnlyList<FileLockingProcess>> GetLockingProcessesAsync(string filePath, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                try
                {
                    var rawProcesses = RestartManager.GetLockingProcesses(filePath);
                    return (IReadOnlyList<FileLockingProcess>)
                        [.. rawProcesses.Select(p => new FileLockingProcess(p.ProcessId, p.ApplicationName, MapApplicationType(p.ApplicationType), p.IsRestartable))];
                }
                catch (InvalidOperationException ex)
                {
                    // A Restart Manager API failure shouldn't surface as an unhandled exception to
                    // the MCP caller - the same "couldn't determine it, report as unknown rather than
                    // erroring the whole call" instinct this codebase already applies elsewhere (e.g.
                    // GitRepositoryStatsProvider's own graceful-degradation precedent).
                    logger.LogWarning(ex, "Restart Manager failed to determine locking processes for {FilePath}.", filePath);
                    return (IReadOnlyList<FileLockingProcess>)[];
                }
            },
            cancellationToken);

    private static FileLockApplicationType MapApplicationType(int rawApplicationType) => rawApplicationType switch
    {
        1 => FileLockApplicationType.MainWindow,
        2 => FileLockApplicationType.OtherWindow,
        3 => FileLockApplicationType.Service,
        4 => FileLockApplicationType.Explorer,
        5 => FileLockApplicationType.Console,
        1000 => FileLockApplicationType.Critical,
        _ => FileLockApplicationType.Unknown,
    };
}
