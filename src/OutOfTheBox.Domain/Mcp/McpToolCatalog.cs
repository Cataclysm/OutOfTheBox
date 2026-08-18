// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Execution;

namespace OutOfTheBox.Domain.Mcp;

/// <summary>
/// The fixed, complete set of every <see cref="McpToolPermissionEntry.Key"/> this service ever
/// recognizes - the nineteen plain MCP tools (every tool except <c>dotnet_run</c>/<c>git_run</c>,
/// which are governed entirely by their own subcommand keys instead of a single master key) plus one
/// key per <see cref="CommandSubcommandPolicy"/> catalog entry. What the MCP Settings dashboard page
/// renders and groups, what a fresh <c>McpToolPermissions</c> row gets seeded with the first time it's
/// seen, and what <c>get_mcp_permissions</c> reports back to an MCP caller.
/// </summary>
public static class McpToolCatalog
{
    /// <summary>Every plain (non-dotnet/git-subcommand) tool's key - its own MCP tool name, unchanged.</summary>
    public static readonly IReadOnlyList<string> PlainToolKeys =
    [
        "read_run_output", "cancel_run", "get_run_resources",
        "list_repositories", "delete_repository", "clone_repository",
        "find_files", "get_file_info", "delete_path", "transfer_file", "get_file_lock_info",
        "authorize_git_host", "list_authorized_git_hosts", "revoke_git_host_authorization",
        "authorize_nuget_feed", "list_authorized_nuget_feeds", "revoke_nuget_feed_authorization",
        "get_environment_info", "get_mcp_permissions",
    ];

    // Every plain tool that mutates state at all - creates, deletes, or stores/removes a credential -
    // defaults to disabled per direct instruction ("disable all non-safe operations by default").
    // cancel_run is the one deliberate exception: it only ends an in-flight run early, never touches
    // a file or a credential, so it stays alongside the genuinely read-only tools. Everything not in
    // this set (the read-only tools, plus cancel_run) defaults to enabled.
    private static readonly IReadOnlySet<string> DefaultDisabledPlainTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "delete_repository", "clone_repository", "delete_path",
        "authorize_git_host", "revoke_git_host_authorization",
        "authorize_nuget_feed", "revoke_nuget_feed_authorization",
    };

    /// <summary>The subcommand key for <paramref name="executable"/>/<paramref name="subcommand"/>, e.g. <c>"dotnet:publish"</c>.</summary>
    public static string SubcommandKey(string executable, string subcommand) => $"{executable}:{subcommand}";

    /// <summary>Every key this service ever recognizes - the nineteen plain tool keys, then every known dotnet subcommand key, then every known git subcommand key.</summary>
    public static IEnumerable<string> AllKeys()
    {
        foreach (var key in PlainToolKeys)
        {
            yield return key;
        }

        foreach (var subcommand in CommandSubcommandPolicy.KnownSubcommandsFor("dotnet"))
        {
            yield return SubcommandKey("dotnet", subcommand);
        }

        foreach (var subcommand in CommandSubcommandPolicy.KnownSubcommandsFor("git"))
        {
            yield return SubcommandKey("git", subcommand);
        }
    }

    /// <summary>
    /// Whether <paramref name="key"/> defaults to enabled the first time its row is seeded - a plain
    /// tool key defaults to enabled unless it's in <see cref="DefaultDisabledPlainTools"/> (every
    /// tool that creates, deletes, or stores/removes a credential - see that field's own remarks); a
    /// subcommand key defaults per <see cref="CommandSubcommandPolicy.IsDefaultEnabled"/>. An
    /// unrecognized key (not in <see cref="AllKeys"/> at all) defaults to disabled.
    /// </summary>
    public static bool DefaultEnabled(string key)
    {
        if (PlainToolKeys.Contains(key, StringComparer.Ordinal))
        {
            return !DefaultDisabledPlainTools.Contains(key);
        }

        var separatorIndex = key.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var executable = key[..separatorIndex];
        var subcommand = key[(separatorIndex + 1)..];
        return CommandSubcommandPolicy.IsDefaultEnabled(executable, subcommand);
    }
}
