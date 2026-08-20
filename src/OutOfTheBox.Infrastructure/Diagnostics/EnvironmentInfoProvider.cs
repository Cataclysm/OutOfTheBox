// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.ComponentModel;
using System.Diagnostics;
using OutOfTheBox.Application.Diagnostics;
using OutOfTheBox.Application.Execution;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Diagnostics;

/// <inheritdoc cref="IEnvironmentInfoProvider" />
/// <remarks>
/// Unlike <see cref="IInstalledToolVersionsProvider"/> (which this reuses for the dotnet/git version
/// fields rather than re-probing them), nothing here is cached - SDK/workload/NuGet-source/disk-space
/// state can genuinely change while the service keeps running, and caching any of it risks reporting
/// stale state to exactly the caller trying to diagnose a *current* environment problem. Spawns
/// `dotnet --list-sdks`/`dotnet workload list`/`dotnet nuget list source` directly via
/// <see cref="Process"/>, the same internal-one-off-probe pattern
/// <see cref="IInstalledToolVersionsProvider"/>'s own implementation already uses, not
/// <c>IProcessRunner</c> (built for caller-facing, run-tracked, per-repository-locked execution,
/// none of which applies here). The actual text parsing lives in the pure, directly-unit-testable
/// <see cref="EnvironmentInfoParser"/> rather than here. Disk space itself is delegated to
/// <see cref="IRootDirectoryDiskSpaceProvider"/> - see that type's own remarks for why it's a
/// separate provider rather than a private method here.
/// </remarks>
public sealed class EnvironmentInfoProvider(
    IInstalledToolVersionsProvider installedToolVersionsProvider,
    IRootDirectoryDiskSpaceProvider diskSpaceProvider,
    ILogger<EnvironmentInfoProvider> logger) : IEnvironmentInfoProvider
{
    /// <inheritdoc />
    public async Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken)
    {
        var toolVersions = await installedToolVersionsProvider.GetVersionsAsync(cancellationToken);

        var sdkOutput = await RunCommandAsync("dotnet", ["--list-sdks"], cancellationToken);
        var workloadOutput = await RunCommandAsync("dotnet", ["workload", "list"], cancellationToken);
        var nugetSourceOutput = await RunCommandAsync("dotnet", ["nuget", "list", "source"], cancellationToken);
        var diskSpace = await diskSpaceProvider.GetDiskSpaceAsync(cancellationToken);

        return new EnvironmentInfo(
            toolVersions.DotnetVersion,
            toolVersions.GitVersion,
            EnvironmentInfoParser.ParseSdkList(sdkOutput),
            EnvironmentInfoParser.ParseWorkloadList(workloadOutput),
            EnvironmentInfoParser.ParseNuGetSourceList(nugetSourceOutput),
            diskSpace);
    }

    // Mirrors InstalledToolVersionsProvider.RunVersionCommandAsync's own shape (see that type's
    // remarks) - a plain one-off Process spawn, not IProcessRunner, with the same
    // "not on PATH for this account" Win32Exception handling precedent.
    private async Task<string?> RunCommandAsync(string executable, string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode == 0 ? output : null;
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "{Executable} {Arguments} failed to start.", executable, string.Join(' ', arguments));
            return null;
        }
    }
}
