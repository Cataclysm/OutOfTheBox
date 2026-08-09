// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Diagnostics;

/// <summary>One .NET SDK installed on the host, as reported by <c>dotnet --list-sdks</c>.</summary>
public sealed record InstalledSdk(string Version, string Path);

/// <summary>One configured NuGet package source, as reported by <c>dotnet nuget list source</c>.</summary>
public sealed record NuGetSource(string Name, string Url, bool IsEnabled);

/// <summary>Available/total disk space for a single drive.</summary>
public sealed record DiskSpaceInfo(long TotalBytes, long AvailableFreeBytes);

/// <summary>
/// This host's installed .NET/git toolchain, configured NuGet sources, and available disk space -
/// per <see cref="IEnvironmentInfoProvider"/>, for diagnosing a restore/build failure caused by an
/// environment mismatch rather than a code problem.
/// </summary>
/// <param name="DotnetVersion">The installed <c>dotnet</c> version, or <see langword="null"/> if it could not be determined.</param>
/// <param name="GitVersion">The installed <c>git</c> version, or <see langword="null"/> if it could not be determined.</param>
/// <param name="InstalledSdks">Every .NET SDK installed on the host.</param>
/// <param name="InstalledWorkloadIds">Installed .NET workload ids - best-effort, empty if the probe failed or nothing is installed.</param>
/// <param name="NuGetSources">Configured NuGet package sources.</param>
/// <param name="RootDirectoryDiskSpace">Disk space for the drive containing the configured root directory.</param>
public sealed record EnvironmentInfo(
    string? DotnetVersion,
    string? GitVersion,
    IReadOnlyList<InstalledSdk> InstalledSdks,
    IReadOnlyList<string> InstalledWorkloadIds,
    IReadOnlyList<NuGetSource> NuGetSources,
    DiskSpaceInfo RootDirectoryDiskSpace);
