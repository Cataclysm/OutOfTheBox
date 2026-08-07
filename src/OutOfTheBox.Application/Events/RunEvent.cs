// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Runs;

namespace OutOfTheBox.Application.Events;

/// <summary>
/// One notification published to <see cref="IRunEventBus"/>. Carries only enough for a subscriber
/// to decide whether to act and, if so, what to re-fetch - not the full run record itself, since
/// the authoritative up-to-date data (for both in-flight and completed runs) always lives in the
/// <c>Runs</c> table via <see cref="Persistence.IRunRepository"/>, which a subscriber re-queries on
/// receiving one of these rather than trusting a payload that could already be stale by the time it
/// renders. <see cref="RepoPath"/> is the one exception - the repository-stats background sampler
/// (specs/repository-management) needs to know *which* repo to recompute on a terminal event
/// without a database round-trip just to find out.
/// </summary>
public sealed record RunEvent(Guid RunId, RunKind Kind, RunEventType Type, string RepoPath)
{
    /// <summary>Which stream the line came from (<c>"stdout"</c> or <c>"stderr"</c>). Set only when <see cref="Type"/> is <see cref="RunEventType.OutputLine"/>.</summary>
    public string? OutputStream { get; init; }

    /// <summary>The produced line's text. Set only when <see cref="Type"/> is <see cref="RunEventType.OutputLine"/>.</summary>
    public string? OutputLine { get; init; }
}
