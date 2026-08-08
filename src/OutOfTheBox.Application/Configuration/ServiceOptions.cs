// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Configuration;

/// <summary>
/// Strongly-typed binding of the <c>OutOfTheBox</c> configuration section, sourced from
/// <c>appsettings.json</c> and overridable via environment variables
/// (e.g. <c>OutOfTheBox__BearerToken</c>).
/// </summary>
public sealed class ServiceOptions
{
    /// <summary>The configuration section name this type binds to.</summary>
    public const string SectionName = "OutOfTheBox";

    /// <summary>
    /// Absolute path to the root directory under which all repository working directories must resolve.
    /// Requests whose resolved working directory falls outside this root are rejected.
    /// </summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>
    /// The shared bearer credential required on every authenticated request and on the dashboard
    /// login page. Read from configuration only - never hardcoded.
    /// </summary>
    public string BearerToken { get; set; } = string.Empty;

    /// <summary>
    /// Default execution timeout applied when a caller does not specify one, in seconds.
    /// </summary>
    public int DefaultExecutionTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Maximum execution timeout, in seconds. A caller-supplied timeout longer than this value is
    /// clamped to it; the default is also never allowed to exceed it.
    /// </summary>
    public int MaximumExecutionTimeoutSeconds { get; set; } = 3600;

    /// <summary>
    /// Maximum number of bytes of stdout/stderr forwarded (and persisted) per execution before the
    /// stream is truncated.
    /// </summary>
    public long OutputCapBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Absolute path to the SQLite database file used for run history and resource-sample persistence.
    /// </summary>
    public string SqliteFilePath { get; set; } = string.Empty;

    /// <summary>
    /// How often the background repository-stats sampler recomputes size/git status for every
    /// repository, in seconds. Deliberately slow (default 60s) relative to the resource sampler -
    /// per design.md, both size and git status are also recomputed immediately whenever a run
    /// against that specific repository reaches a terminal state, so this interval only bounds the
    /// worst-case staleness for a repository nothing has run against recently.
    /// </summary>
    public int RepositoryStatsSamplerIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// How often the background host/process resource sampler ticks, in seconds. Deliberately
    /// fast (default 3s) relative to the repository-stats sampler, per design.md's "a few seconds"
    /// requirement for resource monitoring - this drives both the live Status-view graphs and the
    /// persisted <c>RunResourceSamples</c> series.
    /// </summary>
    public int ResourceSamplerIntervalSeconds { get; set; } = 3;
}
