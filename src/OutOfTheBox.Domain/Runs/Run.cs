// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Runs;

/// <summary>
/// A durable record of one run of any <see cref="RunKind"/> - a <c>dotnet</c>/<c>git</c> command,
/// an artifact transfer, or a repository clone/delete - from the moment it starts through its
/// terminal state. Which fields are meaningful depends on <see cref="Kind"/>: <see cref="Arguments"/>/
/// <see cref="ExitCode"/>/<see cref="Stdout"/>/<see cref="Stderr"/>/<see cref="Truncated"/> apply to
/// command runs (<see cref="RunKind.DotnetCommand"/>/<see cref="RunKind.GitCommand"/>) and clones
/// (<see cref="RunKind.RepositoryClone"/>, which also sets <see cref="SourceUrl"/>);
/// <see cref="ArtifactPath"/>/<see cref="ArtifactSizeBytes"/> apply only to
/// <see cref="RunKind.ArtifactTransfer"/>; a <see cref="RunKind.RepositoryDelete"/> uses none of
/// the kind-specific fields, only the common ones. A plain data holder with zero framework
/// dependency - EF Core's mapping (Infrastructure) knows how to persist it, this type doesn't know
/// EF Core exists.
/// </summary>
public sealed class Run
{
    /// <summary>The run's unique identifier, assigned by the endpoint that accepted it.</summary>
    public required Guid Id { get; init; }

    /// <summary>Which capability produced this run.</summary>
    public required RunKind Kind { get; init; }

    /// <summary>The resolved repository path the run targeted.</summary>
    public required string RepoPath { get; init; }

    /// <summary>The command's argument list, JSON-encoded. Populated for <c>dotnet</c>/<c>git</c>/clone runs only.</summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    /// <summary>The requested file path, relative to the repo. Populated for artifact transfers only.</summary>
    public string? ArtifactPath { get; init; }

    /// <summary>The transferred file's size in bytes, set once a transfer completes successfully.</summary>
    public long? ArtifactSizeBytes { get; set; }

    /// <summary>The clone source URL. Populated for repository clones only.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>When the run started (repo lock acquired, or the transfer/delete began).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the run reached a terminal state; <see langword="null"/> while still in flight.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The run's current or terminal outcome.</summary>
    public required RunOutcome Outcome { get; set; }

    /// <summary>The process exit code, once known. Meaningful for <c>dotnet</c>/<c>git</c>/clone runs only.</summary>
    public int? ExitCode { get; set; }

    /// <summary>Captured standard output, possibly truncated. Meaningful for <c>dotnet</c>/<c>git</c>/clone runs only.</summary>
    public string? Stdout { get; set; }

    /// <summary>Captured standard error, possibly truncated. Meaningful for <c>dotnet</c>/<c>git</c>/clone runs only.</summary>
    public string? Stderr { get; set; }

    /// <summary>Whether the captured output was truncated against the configured output cap.</summary>
    public bool Truncated { get; set; }
}
