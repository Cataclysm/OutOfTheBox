// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>The immediate result of starting a run (<c>dotnet_run</c>/<c>git_run</c>/<c>clone_repository</c>) - returned before the run finishes, per those tools' start-then-poll contract.</summary>
/// <param name="RunId">The started run's id - pass this to <c>read_run_output</c>/<c>cancel_run</c>.</param>
/// <param name="Status">Always <see cref="McpRunStatus.Running"/> at the moment a run is accepted.</param>
public sealed record McpStartRunResult(Guid RunId, string Status);

/// <summary>One <c>read_run_output</c> result: output produced since the requested offset, plus the run's current status.</summary>
/// <param name="Status">One of <see cref="McpRunStatus"/>'s values.</param>
/// <param name="Stdout">Standard output produced since the requested offset (empty if none).</param>
/// <param name="Stderr">Standard error produced since the requested offset (empty if none).</param>
/// <param name="NextOffset">Pass this back on the next <c>read_run_output</c> call to continue reading from here.</param>
/// <param name="Truncated">Whether the run's combined output has exceeded the configured cap.</param>
/// <param name="ExitCode">The process exit code, once <paramref name="Status"/> is <see cref="McpRunStatus.Completed"/>; <see langword="null"/> otherwise.</param>
public sealed record McpReadRunOutputResult(string Status, string Stdout, string Stderr, long NextOffset, bool Truncated, int? ExitCode);

/// <summary>The result of a <c>cancel_run</c> call - the run's status immediately after the cancellation was requested (may still read "running" until the process actually exits; poll <c>read_run_output</c> to observe the terminal transition).</summary>
/// <param name="Status">One of <see cref="McpRunStatus"/>'s values.</param>
public sealed record McpCancelRunResult(string Status);
