// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>How a file was touched by a commit - drives the label the commit detail page shows next to its path.</summary>
public enum CommitFileChangeKind
{
    /// <summary>The file did not exist in the parent commit.</summary>
    Added,

    /// <summary>The file existed in both the parent and this commit, with different content.</summary>
    Modified,

    /// <summary>The file existed in the parent commit but not this one.</summary>
    Deleted,

    /// <summary>The file was moved/renamed from <see cref="CommitFileChange.OldPath"/>.</summary>
    Renamed,

    /// <summary>The file was copied from <see cref="CommitFileChange.OldPath"/>.</summary>
    Copied,

    /// <summary>Any status letter <c>git show --name-status</c> can report beyond the above (e.g. a type change).</summary>
    Other,
}

/// <summary>One file touched by a commit, as shown on the commit detail page.</summary>
/// <param name="Path">The file's path as of this commit (the new path, for a rename/copy).</param>
/// <param name="Kind">How the file was touched.</param>
/// <param name="OldPath">The file's prior path, for <see cref="CommitFileChangeKind.Renamed"/>/<see cref="CommitFileChangeKind.Copied"/> only.</param>
/// <param name="LinesAdded">
/// Lines added, when meaningful to show - <see langword="null"/> for a rename/copy (unreliable to
/// attribute a line count to, since git's own line-stat output identifies those by an "old =&gt;
/// new" arrow notation rather than a plain path) and for a file with no line-based diff at all
/// (e.g. binary content).
/// </param>
/// <param name="LinesRemoved">Lines removed - same availability as <see cref="LinesAdded"/>, and always present or absent together with it.</param>
public sealed record CommitFileChange(string Path, CommitFileChangeKind Kind, string? OldPath = null, int? LinesAdded = null, int? LinesRemoved = null);

/// <summary>
/// The full detail of a single commit - everything <see cref="CommitSummary"/> already carries plus
/// the fields only worth fetching for one commit at a time (full message body, separate committer
/// identity, per-file change list), parsed from <c>git show</c>, per the commit detail subpage's
/// "detailed commit data" requirement.
/// </summary>
public sealed record CommitDetail(
    string Hash,
    string ShortHash,
    IReadOnlyList<string> ParentHashes,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string CommitterName,
    string CommitterEmail,
    DateTimeOffset CommitterDate,
    string Subject,
    string Body,
    IReadOnlyList<CommitRef> Refs,
    IReadOnlyList<CommitFileChange> Files);
