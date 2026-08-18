// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Mcp;

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>
/// How risky a single <see cref="McpToolCatalog"/> key is, purely for the MCP Settings dashboard
/// page's per-row icon/tooltip - has no bearing on enforcement (<c>IMcpPermissionStore</c> and every
/// MCP tool's permission gate are entirely unaware this type exists), only on how clearly an operator
/// is warned before turning something on.
/// </summary>
public enum McpPermissionRiskLevel
{
    /// <summary>Confined to the repository and fully recoverable through git or a rebuild, or reveals nothing beyond what's already visible elsewhere in the dashboard.</summary>
    Safe,

    /// <summary>Doesn't destroy or grant anything, but its normal result can reveal host paths, remote/feed URLs, file contents, or other details an operator may not want an MCP caller to see.</summary>
    InfoExposure,

    /// <summary>Can destroy data irrecoverably, reach outside the repository, install/trust something host-wide, or store/expose a credential.</summary>
    Dangerous,
}

/// <summary>One tooltip's worth of explanation for a single <see cref="McpToolCatalog"/> key.</summary>
/// <param name="What">What calling this actually does.</param>
/// <param name="How">The mechanism behind it - the concrete on-host action it takes.</param>
/// <param name="Risk">Why it's classified the way <paramref name="Level"/> says.</param>
/// <param name="Level">The risk level driving the settings page's icon color.</param>
public sealed record McpPermissionTooltip(string What, string How, string Risk, McpPermissionRiskLevel Level);

/// <summary>
/// A human-readable explanation for every key <see cref="McpToolCatalog.AllKeys"/> can ever produce -
/// what the MCP Settings dashboard page's per-row tooltip shows. Kept separate from
/// <see cref="McpToolCatalog"/> itself (Domain) since this is display copy for one specific page, not
/// a business rule; a completeness unit test (not a compile-time link) is what keeps this table in
/// sync as the catalog grows.
/// </summary>
public static class McpPermissionTooltips
{
    private const string DotnetMechanism = "Runs as `dotnet <subcommand> ...` inside the target repository, started via dotnet_run; output streams back through read_run_output.";
    private const string GitMechanism = "Runs as `git <subcommand> ...` inside the target repository, started via git_run; output streams back through read_run_output.";

    private static readonly IReadOnlyDictionary<string, McpPermissionTooltip> Tooltips = BuildTooltips();

    /// <summary>Every key this table has a tooltip for - should always exactly match <see cref="McpToolCatalog.AllKeys"/>.</summary>
    public static IEnumerable<string> Keys => Tooltips.Keys;

    /// <summary>The tooltip for <paramref name="key"/>. Throws <see cref="KeyNotFoundException"/> if <paramref name="key"/> isn't one <see cref="McpToolCatalog.AllKeys"/> produces - that would mean this table fell out of sync with the catalog.</summary>
    public static McpPermissionTooltip For(string key) => Tooltips[key];

    private static Dictionary<string, McpPermissionTooltip> BuildTooltips()
    {
        var result = new Dictionary<string, McpPermissionTooltip>(StringComparer.Ordinal);

        void Dotnet(string subcommand, string what, string risk, McpPermissionRiskLevel level) =>
            result[McpToolCatalog.SubcommandKey("dotnet", subcommand)] = new McpPermissionTooltip(what, DotnetMechanism, risk, level);

        void Git(string subcommand, string what, string risk, McpPermissionRiskLevel level) =>
            result[McpToolCatalog.SubcommandKey("git", subcommand)] = new McpPermissionTooltip(what, GitMechanism, risk, level);

        void Plain(string key, string what, string how, string risk, McpPermissionRiskLevel level) =>
            result[key] = new McpPermissionTooltip(what, how, risk, level);

        Plain("read_run_output", "Reads the output captured so far for a run started by dotnet_run, git_run, or clone_repository.", "Purely a read from this service's own in-memory buffer; never touches the run's process or the repository.", "No side effects at all.", McpPermissionRiskLevel.Safe);
        Plain("cancel_run", "Stops an in-flight run before it finishes.", "Kills the run's process tree; doesn't touch files or credentials.", "Only ends something already running early - it can't start or change anything.", McpPermissionRiskLevel.Safe);
        Plain("get_run_resources", "Reports a run's recent CPU/RAM usage trend.", "Reads resource data already sampled for the run.", "Read-only diagnostics.", McpPermissionRiskLevel.Safe);
        Plain("list_repositories", "Lists every repository under the configured root.", "Reads directory names, sizes, and git status from the host's filesystem.", "Reveals what repositories exist on this host and their paths/status, though not file contents.", McpPermissionRiskLevel.InfoExposure);
        Plain("delete_repository", "Permanently deletes an entire repository directory.", "Removes the repository's folder and everything in it from disk.", "Irreversible - including any uncommitted local changes.", McpPermissionRiskLevel.Dangerous);
        Plain("clone_repository", "Clones a caller-supplied git URL into a new repository directory.", "Runs `git clone` against whatever URL the caller provides.", "Downloads and stores arbitrary external content onto the host from a caller-controlled address.", McpPermissionRiskLevel.Dangerous);
        Plain("find_files", "Searches a repository's filesystem for files/directories matching a glob pattern.", "Walks the repository's real directory tree; returns matching paths only.", "Reveals file/directory names, not file contents.", McpPermissionRiskLevel.Safe);
        Plain("get_file_info", "Returns filesystem metadata for one file or directory.", "Reads size, timestamps, attributes, and lock state from the filesystem.", "No file content is read.", McpPermissionRiskLevel.Safe);
        Plain("delete_path", "Deletes a file, or a directory and everything under it, within a repository.", "Removes the given path from disk.", "Irreversible.", McpPermissionRiskLevel.Dangerous);
        Plain("transfer_file", "Returns a file's full contents, base64-encoded.", "Reads the file's bytes directly from disk.", "The tool most likely to hand back sensitive data - it returns whatever is in the file, including any secret or credential committed there.", McpPermissionRiskLevel.InfoExposure);
        Plain("get_file_lock_info", "Reports which process has a file open.", "Queries the Windows Restart Manager for handles on the file.", "Reveals a process name/id, not file content.", McpPermissionRiskLevel.Safe);
        Plain("authorize_git_host", "Stores a personal access token for a remote git host.", "Writes the token into the system's git credential helper and this service's own encrypted record.", "A leaked or misused token grants whatever access it carries on the remote host.", McpPermissionRiskLevel.Dangerous);
        Plain("list_authorized_git_hosts", "Lists every git host with a stored credential.", "Reads this service's own credential records; never the token itself.", "Reveals which git hosts/organizations this service is authorized against.", McpPermissionRiskLevel.InfoExposure);
        Plain("revoke_git_host_authorization", "Removes a stored git host credential.", "Deletes it from the git credential helper and this service's own record.", "Only removes access - can't grant, expose, or destroy anything.", McpPermissionRiskLevel.Safe);
        Plain("authorize_nuget_feed", "Stores a personal access token for a NuGet feed.", "Writes the token via the Azure Artifacts Credential Provider or a credentialed NuGet package source.", "A leaked or misused token grants whatever access it carries on that feed.", McpPermissionRiskLevel.Dangerous);
        Plain("list_authorized_nuget_feeds", "Lists every NuGet feed with a stored credential.", "Reads this service's own credential records; never the token itself.", "Reveals internal/private feed URLs this service is authorized against.", McpPermissionRiskLevel.InfoExposure);
        Plain("revoke_nuget_feed_authorization", "Removes a stored NuGet feed credential.", "Deletes it from whichever mechanism backs it.", "Only removes access.", McpPermissionRiskLevel.Safe);
        Plain("get_environment_info", "Reports installed SDKs/workloads, dotnet/git versions, configured NuGet sources, and disk space.", "Queries the host directly; computed fresh on every call.", "Can reveal internal NuGet feed URLs and other host configuration details.", McpPermissionRiskLevel.InfoExposure);
        Plain("get_mcp_permissions", "Reports which tools and subcommands are currently enabled.", "Reads this service's own MCP Settings state.", "Reveals only this service's own configuration, nothing repository- or host-specific.", McpPermissionRiskLevel.Safe);

        Dotnet("restore", "Downloads the NuGet packages a project or solution declares as dependencies.", "Only writes into local NuGet caches and obj folders; doesn't execute project code.", McpPermissionRiskLevel.Safe);
        Dotnet("build", "Compiles the project or solution.", "Writes compiled output into the repository's own bin/obj folders.", McpPermissionRiskLevel.Safe);
        Dotnet("test", "Compiles and runs the repository's test suite.", "Executes the repository's own test code, same as running it locally.", McpPermissionRiskLevel.Safe);
        Dotnet("publish", "Produces a deployable build output, and can push it further (e.g. to a container registry) depending on the project's publish profile.", "Some publish profiles reach a network destination the repository's own project file controls, not just this repository's own folders.", McpPermissionRiskLevel.Dangerous);
        Dotnet("pack", "Builds a local NuGet package (.nupkg) file.", "Writes only a package file into the repository's output folder; doesn't publish it anywhere.", McpPermissionRiskLevel.Safe);
        Dotnet("clean", "Deletes previous build output (bin/obj).", "Only removes generated build artifacts, never source files; a rebuild recreates them.", McpPermissionRiskLevel.Safe);
        Dotnet("run", "Builds and executes the project's own entry point.", "Runs whatever code the project's Main method contains, with no limit on what that code can do (network calls, file writes, reading environment variables) - a much broader execution surface than `test`.", McpPermissionRiskLevel.Dangerous);
        Dotnet("format", "Rewrites source files in place to match formatting/style rules.", "Changes are confined to the repository and fully visible/undoable via `git diff`/`git checkout`.", McpPermissionRiskLevel.Safe);
        Dotnet("nuget", "Manages NuGet package sources and packages - including pushing or deleting a package on a remote feed.", "Can push content to, or delete content from, an external NuGet feed.", McpPermissionRiskLevel.Dangerous);
        Dotnet("workload", "Installs, removes, or repairs .NET SDK workloads.", "Changes this host's shared, machine-wide SDK installation, not just this repository.", McpPermissionRiskLevel.Dangerous);
        Dotnet("tool", "Installs or uninstalls a .NET global or local tool.", "Installs an arbitrary executable package onto the host from a configured feed.", McpPermissionRiskLevel.Dangerous);
        Dotnet("msbuild", "Invokes MSBuild directly against a project, solution, or arbitrary build file.", "MSBuild can run custom targets/tasks (including inline code) with an effectively unconstrained surface.", McpPermissionRiskLevel.Dangerous);
        Dotnet("watch", "Runs the project like `run`, restarting automatically on file changes.", "Same arbitrary-execution surface as `run`, kept running continuously.", McpPermissionRiskLevel.Dangerous);
        Dotnet("sln", "Adds, removes, or lists projects in a .sln file.", "Only edits solution-file metadata; fully visible/undoable via git.", McpPermissionRiskLevel.Safe);
        Dotnet("add", "Adds a package or project reference to a project file.", "Only edits a project file (visible/undoable via git); doesn't fetch or execute the added dependency itself.", McpPermissionRiskLevel.Safe);
        Dotnet("remove", "Removes a package or project reference from a project file.", "Only edits a project file, fully undoable via git.", McpPermissionRiskLevel.Safe);
        Dotnet("list", "Lists a project's package references or project references.", "Read-only report of what's already declared in the repository.", McpPermissionRiskLevel.Safe);
        Dotnet("new", "Scaffolds new files or a new project from a template.", "Only creates new files inside the repository; nothing existing is touched.", McpPermissionRiskLevel.Safe);
        Dotnet("dev-certs", "Creates, trusts, or removes the local ASP.NET Core HTTPS development certificate.", "Changes this host's machine-wide certificate trust store, not just this repository.", McpPermissionRiskLevel.Dangerous);
        Dotnet("user-secrets", "Manages the .NET User Secrets store (list, set, remove, clear) tied to a project.", "Reads or writes a per-user secret store on the host, outside the repository's own files.", McpPermissionRiskLevel.Dangerous);

        Git("fetch", "Downloads new commits/refs from the repository's configured remote.", "Only updates local remote-tracking refs; doesn't touch the working tree.", McpPermissionRiskLevel.Safe);
        Git("checkout", "Switches the working tree to a different branch or commit.", "Standard and git-recoverable; nothing is lost that `git reflog` can't find.", McpPermissionRiskLevel.Safe);
        Git("pull", "Fetches from the remote and merges (or rebases) into the current branch.", "A normal sync operation; resulting conflicts are surfaced, not silently resolved.", McpPermissionRiskLevel.Safe);
        Git("status", "Reports the working tree's current state.", "Read-only.", McpPermissionRiskLevel.Safe);
        Git("log", "Shows commit history.", "Read-only; reveals only what's already in this repository's own history.", McpPermissionRiskLevel.Safe);
        Git("diff", "Shows changes between commits, the working tree, or the index.", "Read-only.", McpPermissionRiskLevel.Safe);
        Git("show", "Shows a single commit's metadata and changes.", "Read-only.", McpPermissionRiskLevel.Safe);
        Git("branch", "Lists, creates, or deletes local branches.", "Deleting a branch only removes a pointer; the commits stay reachable via reflog.", McpPermissionRiskLevel.Safe);
        Git("rev-parse", "Resolves a ref/revision expression to a commit hash or path.", "Read-only.", McpPermissionRiskLevel.Safe);
        Git("push", "Sends local commits to the configured remote.", "Can overwrite shared remote history with a force-push, and exposes local commits externally.", McpPermissionRiskLevel.Dangerous);
        Git("reset", "Moves the current branch pointer, optionally rewriting the working tree/index to match.", "`--hard` discards uncommitted (and staged) changes irrecoverably.", McpPermissionRiskLevel.Dangerous);
        Git("clean", "Deletes files not tracked by git.", "`-xdf` (or similar) permanently deletes untracked and ignored files with no recovery path.", McpPermissionRiskLevel.Dangerous);
        Git("merge", "Combines another branch's history into the current one.", "Adds a merge commit; fully recoverable via reflog even if it goes wrong.", McpPermissionRiskLevel.Safe);
        Git("rebase", "Replays the current branch's commits onto a new base, rewriting them.", "Rewrites commit history - can lose or corrupt commits, especially confusing if the branch is shared elsewhere.", McpPermissionRiskLevel.Dangerous);
        Git("stash", "Shelves uncommitted changes for later.", "Fully recoverable - nothing is discarded.", McpPermissionRiskLevel.Safe);
        Git("cherry-pick", "Applies one existing commit's changes onto the current branch.", "Adds a new commit; doesn't remove or rewrite anything.", McpPermissionRiskLevel.Safe);
        Git("revert", "Creates a new commit that undoes a previous one.", "The non-destructive way to undo history - nothing is deleted.", McpPermissionRiskLevel.Safe);
        Git("tag", "Creates, lists, or deletes a named pointer to a commit.", "Harmless metadata; deleting a tag doesn't touch the commit it pointed to.", McpPermissionRiskLevel.Safe);
        Git("remote", "Adds, removes, or lists this repository's configured remote URLs.", "Reveals (or, if writable, can redirect) where this repository pushes to and pulls from.", McpPermissionRiskLevel.InfoExposure);
        Git("config", "Reads or writes this repository's git configuration.", "Can set values like `core.hooksPath` that point at an arbitrary script run on future git operations - a code-execution vector, not just a settings tweak.", McpPermissionRiskLevel.Dangerous);
        Git("submodule", "Adds, updates, or initializes a submodule - a nested git repository linked at a specific commit.", "Adding or updating one clones an arbitrary, caller-influenced remote - the same risk as clone_repository.", McpPermissionRiskLevel.Dangerous);
        Git("mv", "Renames or moves a tracked file.", "Fully recoverable via git; content is unchanged.", McpPermissionRiskLevel.Safe);
        Git("rm", "Removes a file from both the git index and the working tree.", "The working copy is deleted immediately - the same effective risk as delete_path.", McpPermissionRiskLevel.Dangerous);
        Git("add", "Stages changes for the next commit.", "Only updates the index; no file content changes.", McpPermissionRiskLevel.Safe);
        Git("commit", "Records staged changes as a new commit.", "Always undoable via `git reset`/`git revert`.", McpPermissionRiskLevel.Safe);
        Git("init", "Initializes a new .git directory in a location.", "Low-impact and easily removed; doesn't touch existing files' content.", McpPermissionRiskLevel.Safe);
        Git("apply", "Applies a patch/diff directly to files in the working tree.", "A generic file-editing primitive - it writes whatever changes the patch contains, with no built-in review step.", McpPermissionRiskLevel.Dangerous);
        Git("blame", "Shows which commit last changed each line of a file.", "Read-only.", McpPermissionRiskLevel.Safe);
        Git("worktree", "Creates or manages an additional working-tree checkout linked to this repository.", "Can create a second checkout at another filesystem location - a path that needs the same escape-the-repository scrutiny as any other.", McpPermissionRiskLevel.Dangerous);

        return result;
    }
}
