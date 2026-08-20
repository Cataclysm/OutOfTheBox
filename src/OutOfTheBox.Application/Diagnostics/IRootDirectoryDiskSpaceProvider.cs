// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Diagnostics;

/// <summary>
/// Reports available/total disk space for the drive containing the configured root directory - split
/// out from <see cref="IEnvironmentInfoProvider"/> (which still reuses this for its own
/// <see cref="EnvironmentInfo.RootDirectoryDiskSpace"/> field) so the dashboard's Status page can show
/// it without paying for that provider's much heavier SDK/workload/NuGet-source probing, none of
/// which the page needs.
/// </summary>
public interface IRootDirectoryDiskSpaceProvider
{
    /// <summary>Computes the current disk space - not cached, since free space changes constantly while the service runs.</summary>
    Task<DiskSpaceInfo> GetDiskSpaceAsync(CancellationToken cancellationToken);
}
