// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// One file or folder matched by a glob-pattern search across a repository's own directory tree
/// (the MCP <c>find_files</c> tool) - distinct from <see cref="RepositoryFileEntry"/> (which only
/// ever lists one directory's immediate children, and so has no need for a relative path of its
/// own) since a match can come from anywhere under the repository.
/// </summary>
/// <param name="RelativePath">The entry's path relative to the repository root, using <c>/</c> as the separator regardless of host OS.</param>
/// <param name="Name">The entry's own name (not a path) within its parent directory.</param>
/// <param name="IsDirectory">Whether this entry is a folder.</param>
/// <param name="SizeBytes">The file's size in bytes; <see langword="null"/> for a directory.</param>
/// <param name="LastModifiedUtc">When the entry was last modified on disk.</param>
public sealed record RepositoryEntryMatch(string RelativePath, string Name, bool IsDirectory, long? SizeBytes, DateTimeOffset LastModifiedUtc);
