// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// Full filesystem metadata for one entry within a repository (the MCP <c>get_file_info</c> tool) -
/// deliberately a plain <see langword="bool"/>? for <see cref="IsLocked"/> rather than the richer
/// per-process detail <c>IFileLockInspector</c> returns, so this type has no dependency on
/// <c>OutOfTheBox.Application</c> (the full locking-process list stays the dedicated
/// <c>get_file_lock_info</c> tool's own job).
/// </summary>
/// <param name="RelativePath">The entry's path relative to the repository root, using <c>/</c> as the separator regardless of host OS.</param>
/// <param name="Name">The entry's own name (not a path) within its parent directory.</param>
/// <param name="IsDirectory">Whether this entry is a folder.</param>
/// <param name="SizeBytes">The file's size in bytes; <see langword="null"/> for a directory (a directory's recursive total size is not computed here - see design.md).</param>
/// <param name="Attributes">The entry's Windows file attribute flags (ReadOnly, Hidden, System, Archive, ReparsePoint, ...).</param>
/// <param name="CreatedUtc">When the entry was created on disk.</param>
/// <param name="LastModifiedUtc">When the entry was last modified on disk.</param>
/// <param name="LastAccessedUtc">When the entry was last accessed on disk.</param>
/// <param name="Owner">The entry's owning account, as a friendly name (falling back to a raw SID if it can't be resolved); <see langword="null"/> if the ACL itself couldn't be read.</param>
/// <param name="IsLocked">Whether another process currently has this file open; <see langword="null"/> for a directory, since Restart Manager's own lock model is file-based.</param>
public sealed record RepositoryEntryMetadata(
    string RelativePath,
    string Name,
    bool IsDirectory,
    long? SizeBytes,
    FileAttributes Attributes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    DateTimeOffset LastAccessedUtc,
    string? Owner,
    bool? IsLocked);
