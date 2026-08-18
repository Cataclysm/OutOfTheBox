// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// Deletes a directory or file, working around two real Windows-specific failure modes found on
/// actual machine use - both by <see cref="RepositoryManager.DeleteAsync"/> (a whole repository)
/// and <see cref="RepositoryFileBrowser"/> (a single file/folder inside one), previously each with
/// its own copy of the read-only workaround and neither with any retry at all:
/// <list type="bullet">
/// <item>A read-only file anywhere in the tree (git itself sometimes leaves pack/object files
/// read-only) makes a plain <see cref="Directory.Delete(string, bool)"/> throw
/// <see cref="UnauthorizedAccessException"/> instead of just deleting it - cleared first via
/// <see cref="ClearReadOnlyAttributes"/>, the standard workaround.</item>
/// <item>Even after every file under a directory is gone, the final directory-removal step can
/// still intermittently fail with an "in use"/"not empty" <see cref="IOException"/> for a brief
/// window - Windows, an AV real-time scanner, or the search indexer transiently holding its own
/// handle on the directory entry itself, not any file inside it (reported from real-machine use:
/// deletion cleared every file, then failed on the folder itself, succeeding on an immediate
/// manual retry). Retried with a short backoff instead of failing outright on the first attempt,
/// since the condition reliably clears within a second or two in practice.
/// </item>
/// </list>
/// </summary>
internal static class RecursiveDelete
{
    private const int MaxAttempts = 5;

    /// <summary>Deletes a directory and everything under it, clearing read-only attributes first and retrying briefly on a transient "in use"/"not empty" failure.</summary>
    public static Task DirectoryAsync(string path, CancellationToken cancellationToken) =>
        RetryAsync(
            () =>
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
            },
            cancellationToken);

    /// <summary>Deletes a single file, clearing its read-only attribute first and retrying briefly on a transient "in use" failure.</summary>
    public static Task FileAsync(string path, CancellationToken cancellationToken) =>
        RetryAsync(
            () =>
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            },
            cancellationToken);

    // The `when` filter's own attempt < MaxAttempts check is what makes the final attempt's
    // exception propagate to the caller instead of being swallowed here - callers already have
    // their own catch (IOException or UnauthorizedAccessException) that records a still-genuine
    // failure (e.g. a file truly still open elsewhere) as a failed run with the exception's own
    // message, which retrying forever would just delay rather than fix.
    private static async Task RetryAsync(Action delete, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                delete();
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
    }
}
