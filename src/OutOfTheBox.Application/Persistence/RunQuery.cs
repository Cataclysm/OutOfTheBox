// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Runs;

namespace OutOfTheBox.Application.Persistence;

/// <summary>
/// Filter/search criteria for <see cref="IRunRepository.ListAsync"/>. Every property is optional;
/// a <see langword="null"/> or empty value means "don't filter on this," so an all-default
/// instance returns every run. All supplied criteria combine with AND (per specs/run-history's
/// "Combine filters" and "Search combined with a filter" scenarios).
/// </summary>
public sealed record RunQuery
{
    /// <summary>Restrict to runs of any of these kinds, if supplied.</summary>
    public IReadOnlyCollection<RunKind>? Kinds { get; init; }

    /// <summary>Restrict to runs with any of these outcomes, if supplied.</summary>
    public IReadOnlyCollection<RunOutcome>? Outcomes { get; init; }

    /// <summary>Restrict to runs against exactly this repository path, if supplied.</summary>
    public string? RepositoryPath { get; init; }

    /// <summary>
    /// Free-text query matched against a run's repository, arguments, file path, and clone source
    /// URL (whichever apply to that run's kind), if supplied.
    /// </summary>
    public string? SearchText { get; init; }
}
