// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Diagnostics;

/// <summary>Reports this host's installed .NET/git toolchain, configured NuGet sources, and available disk space, for the <c>get_environment_info</c> MCP tool.</summary>
public interface IEnvironmentInfoProvider
{
    /// <summary>Computes the current environment info - not cached, since SDK/workload/NuGet-source/disk-space state can change while the service runs.</summary>
    Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken);
}
