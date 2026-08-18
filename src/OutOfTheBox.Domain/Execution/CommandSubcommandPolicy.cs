// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Execution;

/// <summary>
/// Pure decision rule: given a fixed executable ("dotnet" or "git") and the subcommand token an MCP
/// caller supplied as the first argument, is it one of the fixed, known set of subcommands
/// <c>dotnet_run</c>/<c>git_run</c> can ever run - a subcommand outside this catalog is permanently
/// blocked, with no setting that can ever re-open it (per direct instruction - the MCP Settings page's
/// <c>IMcpPermissionStore</c> only ever toggles a subcommand that's already in this catalog on or
/// off; it can't add a new one). Checking only this one token
/// (never anything before it in the argument list) also closes git's own global repository-redirect
/// flags (<c>-C</c>, <c>--git-dir=</c>, <c>--work-tree=</c>) as a side effect: those must precede the
/// subcommand positionally, so a caller trying one would put it in this exact slot and simply fail
/// the catalog check - no separate flag denylist is needed for that vector.
/// </summary>
public static class CommandSubcommandPolicy
{
    // The originally-allowed set, from before subcommands became individually configurable - these
    // still default to enabled in MCP Settings; every other catalog entry below defaults to disabled.
    private static readonly IReadOnlySet<string> DefaultEnabledDotnetSubcommands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "restore", "build", "test" };

    private static readonly IReadOnlySet<string> DefaultEnabledGitSubcommands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fetch", "checkout", "pull", "status", "log", "diff", "show", "branch", "rev-parse" };

    private static readonly IReadOnlySet<string> KnownDotnetSubcommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "restore", "build", "test",
        "publish", "pack", "clean", "run", "format", "nuget", "workload", "tool", "msbuild", "watch",
        "sln", "add", "remove", "list", "new", "dev-certs", "user-secrets",
    };

    private static readonly IReadOnlySet<string> KnownGitSubcommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "fetch", "checkout", "pull", "status", "log", "diff", "show", "branch", "rev-parse",
        "push", "reset", "clean", "merge", "rebase", "stash", "cherry-pick", "revert", "tag", "remote",
        "config", "submodule", "mv", "rm", "add", "commit", "init", "apply", "blame", "worktree",
    };

    /// <summary>Whether <paramref name="subcommand"/> is in the known catalog for <paramref name="executable"/> ("dotnet" or "git") at all - independent of whether it's currently enabled. Any other executable is never known.</summary>
    public static bool IsKnownSubcommand(string executable, string subcommand) => executable switch
    {
        "dotnet" => KnownDotnetSubcommands.Contains(subcommand),
        "git" => KnownGitSubcommands.Contains(subcommand),
        _ => false,
    };

    /// <summary>Whether <paramref name="subcommand"/> defaults to enabled in MCP Settings the first time its row is seeded - <see langword="false"/> for any subcommand not even in the catalog.</summary>
    public static bool IsDefaultEnabled(string executable, string subcommand) => executable switch
    {
        "dotnet" => DefaultEnabledDotnetSubcommands.Contains(subcommand),
        "git" => DefaultEnabledGitSubcommands.Contains(subcommand),
        _ => false,
    };

    /// <summary>The full known subcommand catalog for <paramref name="executable"/>, for the MCP Settings page to render and for building a specific rejection message - empty for any other executable.</summary>
    public static IReadOnlyCollection<string> KnownSubcommandsFor(string executable) => executable switch
    {
        "dotnet" => KnownDotnetSubcommands,
        "git" => KnownGitSubcommands,
        _ => [],
    };
}
