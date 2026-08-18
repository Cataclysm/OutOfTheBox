// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>What kind of ref a <see cref="CommitRef"/> names - drives which icon/pill style the commit graph renders it with.</summary>
public enum CommitRefKind
{
    /// <summary>A local branch (e.g. <c>main</c>).</summary>
    LocalBranch,

    /// <summary>A remote-tracking branch (e.g. <c>origin/main</c>).</summary>
    RemoteBranch,

    /// <summary>A tag.</summary>
    Tag,
}

/// <summary>One branch or tag name pointing at a commit, shown as a pill on that commit in the graph.</summary>
/// <param name="Name">The ref's display name - a remote-tracking branch keeps its <c>&lt;remote&gt;/&lt;branch&gt;</c> shape.</param>
/// <param name="Kind">Local branch, remote-tracking branch, or tag.</param>
/// <param name="IsCurrent">Whether this is the branch HEAD currently points to (git's <c>HEAD -&gt;</c> decoration).</param>
public sealed record CommitRef(string Name, CommitRefKind Kind, bool IsCurrent);
