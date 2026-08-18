// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Runs;

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>
/// Human-friendly display text for <see cref="RunKind"/>/<see cref="RunOutcome"/> - the raw enum
/// names (e.g. <c>RepositoryClone</c>, <c>ValidationFailed</c>) read like code identifiers, not
/// something an operator should have to parse. Centralized here rather than duplicated per-file
/// since every dashboard view that lists runs (Status, History, run detail, repository detail)
/// needs the exact same mapping - unlike the small single-line formatting helpers those views
/// duplicate per this project's own established precedent (<c>FormatDateTime</c>/<c>FormatBytes</c>),
/// a 5+ and 8+ case mapping is worth keeping in one place to avoid the views drifting out of sync.
/// </summary>
public static class RunDisplay
{
    /// <summary>
    /// Short label for a run-kind badge/filter, where space is tight - e.g. <c>DotnetCommand</c> →
    /// <c>"DotNET"</c>. Distinct from <see cref="Heading"/>, which is used where more room and more
    /// context is available.
    /// </summary>
    public static string ShortLabel(this RunKind kind) => kind switch
    {
        RunKind.DotnetCommand => "Dotnet",
        RunKind.GitCommand => "Git",
        RunKind.FileTransfer => "Transfer",
        RunKind.RepositoryClone => "Clone",
        RunKind.RepositoryDelete => "Repo Delete",
        RunKind.RepositoryFileDelete => "File Delete",
        _ => kind.ToString(),
    };

    /// <summary>A full, readable label for a run kind - used for page headings, not badges.</summary>
    public static string Heading(this RunKind kind) => kind switch
    {
        RunKind.DotnetCommand => "Dotnet Command",
        RunKind.GitCommand => "Git Command",
        RunKind.FileTransfer => "File Transfer",
        RunKind.RepositoryClone => "Repository Clone",
        RunKind.RepositoryDelete => "Repository Delete",
        RunKind.RepositoryFileDelete => "File Delete",
        _ => kind.ToString(),
    };

    /// <summary>A human-readable label for a run outcome - e.g. <c>TimedOut</c> → <c>"Timed Out"</c>.</summary>
    public static string Label(this RunOutcome outcome) => outcome switch
    {
        RunOutcome.Running => "Running",
        RunOutcome.Completed => "Completed",
        RunOutcome.TimedOut => "Timed Out",
        RunOutcome.Cancelled => "Cancelled",
        RunOutcome.ValidationFailed => "Validation Failed",
        RunOutcome.NotFound => "Not Found",
        RunOutcome.AlreadyExists => "Already Exists",
        RunOutcome.Interrupted => "Interrupted",
        RunOutcome.Failed => "Failed",
        _ => outcome.ToString(),
    };

    /// <summary>
    /// The kind-specific "what happened" detail for a run - the command line for a
    /// <c>dotnet</c>/<c>git</c> run, the requested path for a transfer, the source URL for a clone.
    /// Falls back to the captured error message (<see cref="Run.Stderr"/>) when a kind has no detail
    /// of its own (a delete) and the run failed, so a failure is never a blank cell.
    /// </summary>
    public static string Detail(Run run)
    {
        var kindDetail = run.Kind switch
        {
            RunKind.DotnetCommand => run.Arguments is null ? string.Empty : "dotnet " + string.Join(' ', run.Arguments),
            RunKind.GitCommand => run.Arguments is null ? string.Empty : "git " + string.Join(' ', run.Arguments),
            RunKind.FileTransfer => run.FilePath ?? string.Empty,
            RunKind.RepositoryClone => run.SourceUrl ?? string.Empty,
            RunKind.RepositoryFileDelete => run.FilePath ?? string.Empty,
            _ => string.Empty,
        };

        return kindDetail.Length == 0 && run.Outcome == RunOutcome.Failed && !string.IsNullOrEmpty(run.Stderr)
            ? run.Stderr
            : kindDetail;
    }

    /// <summary>
    /// The repository's short name for list display - <see cref="Run.RepositoryPath"/> is the
    /// resolved absolute path (needed internally for locking/uniqueness), which is exactly the kind
    /// of host implementation detail a list row shouldn't show; the full path remains available on
    /// the run's own detail page.
    /// </summary>
    public static string RepositoryName(Run run) => System.IO.Path.GetFileName(run.RepositoryPath);
}
