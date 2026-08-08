// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System;
using System.Diagnostics;
using System.Linq;
using WixToolset.Dtf.WindowsInstaller;

namespace OutOfTheBox.Msi.CustomActions
{
    /// <summary>
    /// Marks every repository directly under the configured repository root as a trusted git
    /// "safe.directory" - found necessary on real-machine use: git refuses to operate on a
    /// repository whose on-disk owner doesn't match the current user ("dubious ownership",
    /// git's post-CVE-2022-24765 mitigation), and the service account's SID changes on every
    /// uninstall+reinstall (<c>util:CreateUser</c> deletes and recreates the account, and Windows
    /// never reuses a SID), so a repository cloned/owned under a prior install's account fails
    /// every single git command under the new one. Configured via <c>git config --system</c>
    /// (not <c>--global</c>): a system-wide config file lives under git's own Program Files
    /// installation, writable by this already-elevated per-machine install with no dependency on
    /// the service account's user profile existing yet (a freshly created account's profile isn't
    /// created until its first logon, which for a service account is whenever the SCM first starts
    /// it - later than this custom action runs) - and, as a side benefit, a system-wide entry isn't
    /// tied to any one account's SID at all, so it keeps working across a future account
    /// recreation without needing to run again.
    /// </summary>
    public static class ConfigureGitSafeDirectoryAction
    {
        /// <summary>
        /// Builds the safe.directory pattern for <paramref name="repositoryRootDirectory"/> - a
        /// trailing <c>/*</c> (git's own documented syntax for "trust every repository directly
        /// inside this directory", not recursively) matching this service's own flat
        /// one-repo-per-subdirectory layout. Git expects forward slashes for this pattern even on
        /// Windows (its own dubious-ownership error message already renders paths that way).
        /// </summary>
        public static string BuildSafeDirectoryPattern(string repositoryRootDirectory) =>
            repositoryRootDirectory.Replace('\\', '/').TrimEnd('/') + "/*";

        /// <summary>
        /// Adds <paramref name="pattern"/> to git's <paramref name="configScope"/> (<c>--system</c>
        /// in production; <c>--global</c> in tests, which don't run elevated) <c>safe.directory</c>
        /// list, unless it's already present - <c>git config --add</c> is not itself idempotent
        /// (it appends a new value line every call), which would otherwise accumulate a duplicate
        /// entry on every single install/upgrade/repair this action runs during.
        /// </summary>
        public static void EnsureSafeDirectory(string pattern, string configScope)
        {
            if (IsAlreadyConfigured(pattern, configScope))
            {
                return;
            }

            RunGitCapturingOutput(configScope, "--add", "safe.directory", pattern);
        }

        private static bool IsAlreadyConfigured(string pattern, string configScope)
        {
            var (exitCode, output) = RunGitCapturingOutput(configScope, "--get-all", "safe.directory");

            // A non-zero exit means the key has no entries yet at all (git config --get-all exits 1
            // in that case) - not an error, just "not configured yet".
            return exitCode == 0 && output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Contains(pattern, StringComparer.Ordinal);
        }

        private static (int ExitCode, string Output) RunGitCapturingOutput(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                // net472 has no ProcessStartInfo.ArgumentList (added in .NET Core), so the
                // command line is built and quoted by hand - adequate for this narrow, fully
                // known set of arguments (git subcommand keywords plus one path-derived pattern),
                // not a general-purpose command-line quoting routine.
                Arguments = "config " + string.Join(" ", arguments.Select(QuoteArgument)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = Process.Start(startInfo))
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return (process.ExitCode, output);
            }
        }

        private static string QuoteArgument(string argument) =>
            argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";

        /// <summary>
        /// MSI custom action entry point - scheduled in <c>InstallExecuteSequence</c> only, same
        /// reasoning as <see cref="CreateRepositoryRootDirectoryAction"/>: <c>REPOROOTDIR</c> only
        /// holds its final, operator-confirmed value once <c>ConfigDlg</c> has been through. Never
        /// fails the install - this fixes a real friction point, but the service installs and runs
        /// (dotnet commands, and git commands against a repository whose ownership already matches)
        /// perfectly well without it, so it isn't worth a hard install failure over.
        /// </summary>
        [CustomAction]
        public static ActionResult ConfigureGitSafeDirectory(Session session)
        {
            try
            {
                var pattern = BuildSafeDirectoryPattern(session["REPOROOTDIR"]);
                EnsureSafeDirectory(pattern, "--system");
            }
            catch (Exception)
            {
            }

            return ActionResult.Success;
        }
    }
}
