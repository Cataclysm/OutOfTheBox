// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Execution;

/// <summary>
/// Pure decision rule: given a fixed executable ("dotnet" or "git") and the subcommand token an MCP
/// caller supplied as the first argument, is it one of the small set <c>dotnet_run</c>/<c>git_run</c>
/// allow - reversing this project's original "no subcommand/flag allowlist, by explicit request"
/// decision. <c>dotnet</c> is limited to <c>restore</c>/<c>build</c>/<c>test</c> - no <c>publish</c>/
/// <c>pack</c>/<c>run</c>/<c>nuget</c>/<c>workload</c>, none of which this service's actual use case
/// (build and test a repo) needs. <c>git</c> is limited to <c>fetch</c>/<c>checkout</c>/<c>pull</c>
/// plus common read-only inspection commands that can't mutate or escape the repository -
/// <c>status</c>/<c>log</c>/<c>diff</c>/<c>show</c>/<c>branch</c>/<c>rev-parse</c> - no <c>push</c>/
/// <c>reset</c>/<c>clean</c>. Checking only this one token (never anything before it in the argument
/// list) also closes git's own global repository-redirect flags (<c>-C</c>, <c>--git-dir=</c>,
/// <c>--work-tree=</c>) as a side effect: those must precede the subcommand positionally, so a caller
/// trying one would put it in this exact slot and simply fail the allowlist - no separate flag
/// denylist is needed for that vector.
/// </summary>
public static class CommandSubcommandPolicy
{
    private static readonly IReadOnlySet<string> AllowedDotnetSubcommands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "restore", "build", "test" };

    private static readonly IReadOnlySet<string> AllowedGitSubcommands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fetch", "checkout", "pull", "status", "log", "diff", "show", "branch", "rev-parse" };

    /// <summary>Whether <paramref name="subcommand"/> is allowed for <paramref name="executable"/> ("dotnet" or "git"). Any other executable is never allowed.</summary>
    public static bool IsAllowed(string executable, string subcommand) => executable switch
    {
        "dotnet" => AllowedDotnetSubcommands.Contains(subcommand),
        "git" => AllowedGitSubcommands.Contains(subcommand),
        _ => false,
    };

    /// <summary>The full allowed subcommand set for <paramref name="executable"/>, for building a specific rejection message - empty for any other executable.</summary>
    public static IReadOnlyCollection<string> AllowedSubcommandsFor(string executable) => executable switch
    {
        "dotnet" => AllowedDotnetSubcommands,
        "git" => AllowedGitSubcommands,
        _ => [],
    };
}
