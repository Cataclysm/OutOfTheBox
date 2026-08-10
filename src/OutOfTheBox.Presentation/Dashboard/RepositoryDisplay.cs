// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Globalization;
using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>
/// Human-friendly display text for a <see cref="RepositorySummary"/> - centralized per this
/// project's own precedent (see <see cref="RunDisplay"/>) for helpers substantial enough that
/// per-file duplicates risk drifting out of sync, rather than the small single-line formatters
/// this codebase otherwise tolerates duplicating.
/// </summary>
public static class RepositoryDisplay
{
    /// <summary>The git branch/dirty/ahead-behind status text shown in repository list and detail views.</summary>
    public static string GitStatusText(RepositorySummary repository)
    {
        if (!repository.StatsComputed)
        {
            return "Computing…";
        }

        if (!repository.IsGitRepository)
        {
            return "Not a git repository";
        }

        var branchText = repository.IsDetachedHead ? $"detached at {repository.Branch}" : repository.Branch ?? "(no branch)";
        var status = $"{branchText} ({(repository.IsDirty ? "dirty" : "clean")})";

        if (repository.IsRemoteGone)
        {
            status += ", remote gone";
        }
        else
        {
            if (repository.AheadCount is > 0)
            {
                status += $", ahead {repository.AheadCount}";
            }

            if (repository.BehindCount is > 0)
            {
                status += $", behind {repository.BehindCount}";
            }
        }

        return status;
    }

    /// <summary>A human-readable byte size (e.g. <c>"4.2 MB"</c>), invariant-culture-formatted - see RepositoryDetail.razor's own FormatDateTime for why that matters.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{(bytes / (1024.0 * 1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture)} GB",
        >= 1024L * 1024 => $"{(bytes / (1024.0 * 1024)).ToString("F1", CultureInfo.InvariantCulture)} MB",
        >= 1024L => $"{(bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture)} KB",
        _ => $"{bytes} B",
    };
}
