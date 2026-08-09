// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Diagnostics;

/// <summary>Reports this host's installed .NET/git toolchain, configured NuGet sources, and available disk space, for the <c>get_environment_info</c> MCP tool.</summary>
public interface IEnvironmentInfoProvider
{
    /// <summary>Computes the current environment info - not cached, since SDK/workload/NuGet-source/disk-space state can change while the service runs.</summary>
    Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken);
}
