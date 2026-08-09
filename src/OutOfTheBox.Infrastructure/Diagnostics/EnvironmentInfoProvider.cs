// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Diagnostics;
using OutOfTheBox.Application.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// <see cref="EnvironmentInfoParser"/> rather than here.
/// </remarks>
public sealed class EnvironmentInfoProvider(
    IInstalledToolVersionsProvider installedToolVersionsProvider,
    IOptions<ServiceOptions> options,
    ILogger<EnvironmentInfoProvider> logger) : IEnvironmentInfoProvider
{
    /// <inheritdoc />
    public async Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken)
    {
        var toolVersions = await installedToolVersionsProvider.GetVersionsAsync(cancellationToken);

        var sdkOutput = await RunCommandAsync("dotnet", ["--list-sdks"], cancellationToken);
        var workloadOutput = await RunCommandAsync("dotnet", ["workload", "list"], cancellationToken);
        var nugetSourceOutput = await RunCommandAsync("dotnet", ["nuget", "list", "source"], cancellationToken);

        return new EnvironmentInfo(
            toolVersions.DotnetVersion,
            toolVersions.GitVersion,
            EnvironmentInfoParser.ParseSdkList(sdkOutput),
            EnvironmentInfoParser.ParseWorkloadList(workloadOutput),
            EnvironmentInfoParser.ParseNuGetSourceList(nugetSourceOutput),
            GetRootDirectoryDiskSpace());
    }

    private DiskSpaceInfo GetRootDirectoryDiskSpace()
    {
        try
        {
            var root = options.Value.RootDirectory;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return new DiskSpaceInfo(0, 0);
            }

            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? root);
            return new DiskSpaceInfo(drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A configuration/environment problem here shouldn't take down the rest of an otherwise
            // useful response - same "degrade this one field, don't fail the whole call" discipline
            // as EnvironmentInfoParser's own parsers.
            logger.LogWarning(ex, "Failed to read disk space for the configured root directory.");
            return new DiskSpaceInfo(0, 0);
        }
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
