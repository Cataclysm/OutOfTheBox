// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.RegularExpressions;

namespace OutOfTheBox.Application.Diagnostics;

/// <summary>
/// Pure parsing of <c>dotnet --list-sdks</c>/<c>dotnet nuget list source</c>/<c>dotnet workload
/// list</c>'s text output into structured data - no IO, so it's directly unit-testable against
/// captured sample output, the same shape as <c>OutOfTheBox.Domain.Repositories.CommitGraphLayout</c>'s
/// pure graph computation or the previous change's <c>RunResourceTrend</c>. Lives here rather than
/// alongside the process-spawning code that produces this text
/// (<c>OutOfTheBox.Infrastructure.Diagnostics.EnvironmentInfoProvider</c>) specifically so the
/// parsing logic can be tested without spawning a real process.
/// </summary>
public static class EnvironmentInfoParser
{
    private static readonly Regex SdkLinePattern = new(@"^(\S+)\s+\[(.+)\]$", RegexOptions.Compiled);
    private static readonly Regex NuGetSourceLinePattern = new(@"^\s*\d+\.\s+(.+?)\s+\[(Enabled|Disabled)\]\s*$", RegexOptions.Compiled);

    /// <summary>Parses <c>dotnet --list-sdks</c>'s "&lt;version&gt; [&lt;path&gt;]" per-line output.</summary>
    public static IReadOnlyList<InstalledSdk> ParseSdkList(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var sdks = new List<InstalledSdk>();
        foreach (var line in SplitNonEmptyLines(output))
        {
            var match = SdkLinePattern.Match(line);
            if (match.Success)
            {
                sdks.Add(new InstalledSdk(match.Groups[1].Value, match.Groups[2].Value));
            }
        }

        return sdks;
    }

    /// <summary>
    /// Parses <c>dotnet nuget list source</c>'s output: "Registered Sources:" then, per source, a
    /// "N.  &lt;name&gt; [Enabled|Disabled]" line followed by the source URL on its own (indented) line.
    /// </summary>
    public static IReadOnlyList<NuGetSource> ParseNuGetSourceList(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var sources = new List<NuGetSource>();
        var lines = SplitNonEmptyLines(output);

        for (var i = 0; i < lines.Count; i++)
        {
            var match = NuGetSourceLinePattern.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var url = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
            sources.Add(new NuGetSource(match.Groups[1].Value.Trim(), url, string.Equals(match.Groups[2].Value, "Enabled", StringComparison.Ordinal)));
        }

        return sources;
    }

    /// <summary>
    /// Parses <c>dotnet workload list</c>'s output - deliberately best-effort (see design.md):
    /// skips anything that isn't obviously a data row (blank lines, the "Workload version:" banner,
    /// the column header, the dashed separator, the trailing "Use `dotnet workload search`..." hint,
    /// the plain "No workloads installed..." sentence printed when nothing is installed) and takes
    /// the first token of everything else as a workload id. Never throws - an unrecognized future
    /// format just yields an empty list.
    /// </summary>
    public static IReadOnlyList<string> ParseWorkloadList(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var line in SplitNonEmptyLines(output))
        {
            if (line.StartsWith("Workload version:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Installed Workload", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Use ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("No workloads installed", StringComparison.OrdinalIgnoreCase) ||
                line.TrimStart('-').Length == 0)
            {
                continue;
            }

            var firstToken = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(firstToken))
            {
                ids.Add(firstToken);
            }
        }

        return ids;
    }

    private static IReadOnlyList<string> SplitNonEmptyLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToList();
}
