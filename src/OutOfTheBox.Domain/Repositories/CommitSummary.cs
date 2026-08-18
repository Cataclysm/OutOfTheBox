// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>One commit as shown in the repository detail commit graph - parsed from <c>git log</c>, per specs/repository-management's "Repository detail shows a branch-aware commit graph" requirement.</summary>
public sealed record CommitSummary(
    string Hash,
    string ShortHash,
    IReadOnlyList<string> ParentHashes,
    string AuthorName,
    DateTimeOffset AuthorDate,
    string Subject,
    IReadOnlyList<CommitRef> Refs);
