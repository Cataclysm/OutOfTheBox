// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using WixToolset.Dtf.WindowsInstaller;

// So CreateRepositoryRootDirectoryTests can exercise the retry-then-fail path with a
// fast/short-delay retry policy instead of production's real (slower, deliberately so)
// SidResolveMaxAttempts/SidResolveRetryDelay.
[assembly: InternalsVisibleTo("OutOfTheBox.Msi.CustomActions.Tests")]

namespace OutOfTheBox.Msi.CustomActions
{
    /// <summary>
    /// Ensures the operator-supplied repository root directory (<c>REPOSITORYROOTDIR</c>, the
    /// <c>ConfigDlg</c> field bound to <c>OutOfTheBox__RootDirectory</c>) exists before the service
    /// starts, so a fresh path typed into the config dialog doesn't leave the service pointed at a
    /// directory that was never created - a real gap, since nothing else in the install ever
    /// touches this path. Also grants the service account explicit NTFS rights on it, recursively -
    /// see <see cref="GrantServiceAccountAccess(string, string, string)"/>'s own remarks for why this is necessary and not
    /// just belt-and-suspenders.
    /// </summary>
    public static class CreateRepositoryRootDirectoryAction
    {
        /// <summary>
        /// Creates <paramref name="path"/> if it doesn't already exist. A no-op - not a
        /// clear-and-recreate - when it does, per <see cref="Directory.CreateDirectory(string)"/>'s
        /// own semantics: an existing repository root's contents (the operator's actual repositories) must
        /// never be touched.
        /// </summary>
        public static void EnsureExists(string path) => Directory.CreateDirectory(path);

        /// <summary>
        /// Grants <paramref name="accountDomain"/>\<paramref name="accountName"/> full control of
        /// <paramref name="path"/> - the directory itself (with inheritance, so newly created
        /// repositories automatically pick it up) and, since inheritance only ever applies going
        /// forward, every already-existing file and subdirectory too (a repository cloned before
        /// this fix ran, or by a different identity entirely, e.g. an operator's own interactive
        /// <c>git clone</c>).
        ///
        /// Found necessary on real-machine use: <see cref="Directory.CreateDirectory(string)"/>
        /// above gives the new directory whatever ACL its parent's default inheritance provides,
        /// which does not include the dedicated, non-administrator service account this MSI creates
        /// (<c>util:User</c>, per design.md's least-privilege risk mitigation) - unlike the data
        /// directory, which gets an explicit grant via <c>util:PermissionEx</c> in <c>Package.wxs</c>
        /// because its path is fixed at compile time, the repository root's path is only known at
        /// install time (it's operator-typed), so the equivalent grant has to happen imperatively
        /// here instead of declaratively in WiX. Deletion is where this surfaced concretely: an
        /// operator's own git checkout containing files (e.g. pack objects under
        /// <c>.git\objects\pack\</c>) the service account had no ACL entry for at all failed with
        /// <c>UnauthorizedAccessException</c> ("Access ... is denied") even after clearing the
        /// read-only attribute (a genuinely different problem - a missing ACE, not a set attribute)
        /// - reachable by any operator who created content in the repository root before installing,
        /// or manually alongside the service, not just a same-class variant of the account-SID-
        /// recreation problem <c>ConfigureGitSafeDirectoryAction</c> already documents for git's own
        /// trust check.
        ///
        /// <paramref name="accountDomain"/> is required, not optional, despite <see cref="NTAccount"/>
        /// having a single-string constructor that looks like it should accept a bare name - a bare
        /// <c>new NTAccount("svc-outofthebox")</c> (no domain qualifier) also threw
        /// <see cref="IdentityNotMappedException"/> on real-machine use, while the qualified
        /// <c>.\svc-outofthebox</c> form <c>Package.wxs</c>'s own
        /// <c>ServiceInstall/@Account="[SERVICEACCOUNTDOMAIN]\[SERVICEACCOUNTNAME]"</c> already uses
        /// successfully does resolve - not itself sufficient (see <see cref="ResolveSidWithRetry"/>'s
        /// own remarks for the actual remaining cause), but a real fix in its own right, not just a
        /// red herring.
        /// </summary>
        public static void GrantServiceAccountAccess(string path, string accountDomain, string accountName) =>
            GrantServiceAccountAccess(path, accountDomain, accountName, SidResolveMaxAttempts, SidResolveRetryDelay);

        /// <summary>
        /// Same as <see cref="GrantServiceAccountAccess(string, string, string)"/>, with the SID
        /// resolution retry policy overridable - exists so tests can exercise the retry-then-fail
        /// path (a genuinely nonexistent account) in milliseconds instead of the real
        /// <see cref="SidResolveMaxAttempts"/> x <see cref="SidResolveRetryDelay"/> the production
        /// path deliberately waits, per <see cref="ResolveSidWithRetry"/>'s own remarks.
        /// </summary>
        internal static void GrantServiceAccountAccess(string path, string accountDomain, string accountName, int maxAttempts, TimeSpan retryDelay)
        {
            var account = new NTAccount(accountDomain, accountName);
            var sid = ResolveSidWithRetry(account, maxAttempts, retryDelay);

            // Files can't carry inheritance flags at all (they have no children to propagate to) -
            // .NET's AddAccessRule throws ArgumentException if any are set on a file-targeted rule,
            // so directories and files need their own rule instances, not one shared between them.
            var directoryRule = new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            var fileRule = new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow);

            ApplyRule(new DirectoryInfo(path), directoryRule);

            // Best-effort per item: one inaccessible leftover (e.g. from a prior partial failure)
            // shouldn't stop the rest of the tree from being fixed.
            foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                TryApplyRule(new DirectoryInfo(directory), directoryRule);
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                TryApplyRule(new FileInfo(file), fileRule);
            }
        }

        /// <summary>Production retry count for <see cref="ResolveSidWithRetry"/> - see its own remarks for why this needs to exist at all.</summary>
        private const int SidResolveMaxAttempts = 10;

        /// <summary>Production delay between retries for <see cref="ResolveSidWithRetry"/>.</summary>
        private static readonly TimeSpan SidResolveRetryDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Resolves <paramref name="account"/> to its <see cref="SecurityIdentifier"/>, retrying on
        /// <see cref="IdentityNotMappedException"/> - a documented, real-world timing issue for an
        /// account created moments earlier in the very same process (found on real-machine use:
        /// <c>NTAccount.Translate</c> failed to resolve <c>svc-outofthebox</c> even running fully
        /// deferred and elevated - <c>Impersonate="no"</c>, confirmed via the compiled MSI's
        /// <c>CustomAction</c> table - and even after <c>Wix4CreateUser_X64</c>/<c>InstallServices</c>
        /// had both already completed successfully in the same install, ruling out both the
        /// scheduling-order and impersonation hypotheses this action's history already tried and
        /// rejected). SID/name-translation lookups go through the LSA, whose own local cache can
        /// lag a just-created account by a few seconds even for a fully privileged (SYSTEM) caller -
        /// a handful of retries with a short delay is the standard, documented workaround for
        /// exactly this class of transient failure, not a symptom of anything this code is doing
        /// wrong. Resolving once up front (rather than letting <see cref="FileSystemAccessRule"/> retry
        /// internally on every single file) also means the wait, if any, happens once per install,
        /// not once per file in a possibly-large repository tree.
        /// </summary>
        private static SecurityIdentifier ResolveSidWithRetry(NTAccount account, int maxAttempts, TimeSpan delay)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                }
                catch (IdentityNotMappedException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(delay);
                }
            }

            // One last, un-retried attempt so a genuine (non-transient) failure throws its own
            // real IdentityNotMappedException here, rather than a synthetic one that would hide
            // which account/step actually failed from the caller's own exception handling/logging.
            return (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
        }

        private static void TryApplyRule(FileSystemInfo item, FileSystemAccessRule rule)
        {
            try
            {
                ApplyRule(item, rule);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
            }
        }

        private static void ApplyRule(FileSystemInfo item, FileSystemAccessRule rule)
        {
            switch (item)
            {
                case DirectoryInfo directory:
                    var directorySecurity = directory.GetAccessControl();
                    directorySecurity.AddAccessRule(rule);
                    directory.SetAccessControl(directorySecurity);
                    break;
                case FileInfo file:
                    var fileSecurity = file.GetAccessControl();
                    fileSecurity.AddAccessRule(rule);
                    file.SetAccessControl(fileSecurity);
                    break;
            }
        }

        /// <summary>
        /// MSI custom action entry point - scheduled in <c>InstallExecuteSequence</c> only (not
        /// <c>InstallUISequence</c>, unlike <c>ResolveSecrets</c>): <c>REPOSITORYROOTDIR</c> only holds
        /// its final, operator-confirmed value once <c>ConfigDlg</c> has been through and the
        /// installer has moved on to actually installing - running this any earlier would create
        /// the property's still-default value (<c>C:\repositories</c>) before the operator ever got a
        /// chance to type a different path. Runs on every install and upgrade, not just a fresh
        /// install - re-granting an already-held right is harmless, and an upgrade is exactly when a
        /// repository root with pre-existing content most needs this applied.
        ///
        /// <see cref="GrantServiceAccountAccess(string, string, string)"/> already tolerates a failure on any individual
        /// file or subdirectory (<see cref="TryApplyRule"/>'s own catch) - one inaccessible leftover
        /// shouldn't stop the rest of the tree from being fixed. What reaches here is different: a
        /// failure applying the rule to the root itself, meaning the grant didn't happen *at all*,
        /// not just partially. Logged with full exception detail and, deliberately unlike
        /// <c>ConfigureGitSafeDirectoryAction</c>'s own "never fail the install" precedent, returned
        /// as a real <see cref="ActionResult.Failure"/>: a silently "successful" install that leaves
        /// this action's entire reason for existing quietly not done is worse than a loud, actionable
        /// install failure - reasoning through the original scheduling bug (account created after
        /// this action ran, caught only by an actual failed install) is exactly what makes the
        /// alternative - swallow-and-continue - the wrong default here.
        /// </summary>
        [CustomAction]
        public static ActionResult CreateRepositoryRootDirectory(Session session)
        {
            // Deferred + Impersonate="no" (see this action's own Package.wxs authoring for why) can
            // only read CustomActionData, not the live session property table directly - Session's
            // own indexer would silently return empty strings here instead of the real values, since
            // deferred actions don't have access to the property table at all, only whatever an
            // earlier immediate action packed into CustomActionData for them.
            var path = session.CustomActionData["REPOSITORYROOTDIR"];
            var accountDomain = session.CustomActionData["SERVICEACCOUNTDOMAIN"];
            var accountName = session.CustomActionData["SERVICEACCOUNTNAME"];
            EnsureExists(path);

            try
            {
                GrantServiceAccountAccess(path, accountDomain, accountName);
            }
            catch (Exception ex)
            {
                // The account string itself is logged explicitly, not just left to whatever the
                // exception's own message happens to include (IdentityNotMappedException's is a
                // generic "Some or all identity references could not be translated," naming no
                // account at all) - found necessary while diagnosing this exact failure the first
                // time, from a log that otherwise gave no way to tell which identity was attempted.
                session.Log("CreateRepositoryRootDirectory: failed to grant '{0}\\{1}' access to '{2}': {3}", accountDomain, accountName, path, ex);
                return ActionResult.Failure;
            }

            return ActionResult.Success;
        }
    }
}
