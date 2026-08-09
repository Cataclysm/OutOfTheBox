// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>One file or folder entry in a repository's file tree browser.</summary>
/// <param name="Name">The entry's own name (not a path) within its parent directory.</param>
/// <param name="IsDirectory">Whether this entry is a folder.</param>
/// <param name="SizeBytes">The file's size in bytes; <see langword="null"/> for a directory.</param>
/// <param name="LastModifiedUtc">When the entry was last modified on disk.</param>
public sealed record RepositoryFileEntry(string Name, bool IsDirectory, long? SizeBytes, DateTimeOffset LastModifiedUtc);
