// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Diagnostics;

/// <inheritdoc cref="IRootDirectoryDiskSpaceProvider" />
/// <remarks>Pure <see cref="DriveInfo"/> lookup - no process spawned, unlike the sibling providers this sits next to.</remarks>
public sealed class RootDirectoryDiskSpaceProvider(
    IOptions<ServiceOptions> options,
    ILogger<RootDirectoryDiskSpaceProvider> logger) : IRootDirectoryDiskSpaceProvider
{
    /// <inheritdoc />
    public Task<DiskSpaceInfo> GetDiskSpaceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var root = options.Value.RootDirectory;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return Task.FromResult(new DiskSpaceInfo(0, 0));
            }

            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? root);
            return Task.FromResult(new DiskSpaceInfo(drive.TotalSize, drive.AvailableFreeSpace));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A configuration/environment problem here shouldn't take down the page/response that
            // asked for it - same "degrade this one field, don't fail the whole call" discipline
            // EnvironmentInfoParser's own parsers already follow.
            logger.LogWarning(ex, "Failed to read disk space for the configured root directory.");
            return Task.FromResult(new DiskSpaceInfo(0, 0));
        }
    }
}
