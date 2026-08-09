// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Diagnostics;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// The <c>get_environment_info</c> MCP tool, per <c>mcp-environment-info</c>'s spec: lets a caller
/// inspect this host's installed .NET/git toolchain, configured NuGet sources, and available disk
/// space, to diagnose a restore/build failure caused by an environment mismatch rather than a code
/// problem. A thin mapping over <see cref="IEnvironmentInfoProvider"/> - see that type's own remarks
/// for why nothing here is cached.
/// </summary>
[McpServerToolType]
public sealed class EnvironmentInfoMcpTools(IEnvironmentInfoProvider environmentInfoProvider)
{
    /// <summary>Returns this host's installed .NET/git toolchain, configured NuGet sources, and available disk space.</summary>
    [McpServerTool]
    [Description("Returns this host's installed dotnet/git versions, installed .NET SDKs, installed .NET workloads (best-effort - empty if this host's SDK doesn't support listing them), configured NuGet package sources, and available disk space on the configured root directory's drive. Use this to diagnose a restore/build failure caused by a missing SDK/workload, an unreachable or misconfigured NuGet feed, or insufficient disk space, rather than a code problem. Computed fresh on every call, not cached.")]
    public async Task<McpEnvironmentInfoResult> GetEnvironmentInfoAsync()
    {
        var info = await environmentInfoProvider.GetEnvironmentInfoAsync(CancellationToken.None);

        return new McpEnvironmentInfoResult(
            info.DotnetVersion,
            info.GitVersion,
            [.. info.InstalledSdks.Select(sdk => new McpInstalledSdk(sdk.Version, sdk.Path))],
            info.InstalledWorkloadIds,
            [.. info.NuGetSources.Select(source => new McpNuGetSource(source.Name, source.Url, source.IsEnabled))],
            new McpDiskSpaceInfo(info.RootDirectoryDiskSpace.TotalBytes, info.RootDirectoryDiskSpace.AvailableFreeBytes));
    }
}

/// <summary>One .NET SDK installed on the host.</summary>
public sealed record McpInstalledSdk(string Version, string Path);

/// <summary>One configured NuGet package source.</summary>
public sealed record McpNuGetSource(string Name, string Url, bool IsEnabled);

/// <summary>Available/total disk space for the configured root directory's drive.</summary>
public sealed record McpDiskSpaceInfo(long TotalBytes, long AvailableFreeBytes);

/// <summary>The result of a <c>get_environment_info</c> call.</summary>
/// <param name="DotnetVersion">The installed <c>dotnet</c> version, or <see langword="null"/> if it could not be determined.</param>
/// <param name="GitVersion">The installed <c>git</c> version, or <see langword="null"/> if it could not be determined.</param>
/// <param name="InstalledSdks">Every .NET SDK installed on the host.</param>
/// <param name="InstalledWorkloadIds">Installed .NET workload ids - best-effort, empty if the probe failed or nothing is installed.</param>
/// <param name="NuGetSources">Configured NuGet package sources.</param>
/// <param name="RootDirectoryDiskSpace">Disk space for the drive containing the configured root directory.</param>
public sealed record McpEnvironmentInfoResult(
    string? DotnetVersion,
    string? GitVersion,
    IReadOnlyList<McpInstalledSdk> InstalledSdks,
    IReadOnlyList<string> InstalledWorkloadIds,
    IReadOnlyList<McpNuGetSource> NuGetSources,
    McpDiskSpaceInfo RootDirectoryDiskSpace);
