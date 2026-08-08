// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace OutOfTheBox.Msi.CustomActions.Tests
{
    public class CreateRepositoryRootDirectoryTests
    {
        // The real MSI grants svc-outofthebox, which won't exist on a dev/CI machine - the current
        // process' own identity is a stand-in that's guaranteed to exist and, since a temp
        // directory this process creates is always owned by it, doesn't need elevation to modify
        // (WRITE_DAC on an object you own doesn't require any special privilege). Split into domain
        // and bare name, matching GrantServiceAccountAccess's own two-argument shape - a real
        // regression this split guards against: a single pre-qualified "DOMAIN\name" string passed
        // through NTAccount's one-argument constructor is a silently different (and, on a real
        // install, failing) code path from the two-argument constructor the production code uses.
        private static readonly string CurrentIdentityName = WindowsIdentity.GetCurrent().Name;
        private static readonly string TestAccountDomain = CurrentIdentityName.Split('\\')[0];
        private static readonly string TestAccountName = CurrentIdentityName.Split('\\')[1];

        [Fact]
        public void EnsureExists_creates_a_missing_directory()
        {
            var path = Path.Combine(Path.GetTempPath(), "OutOfTheBox-Test-" + Guid.NewGuid().ToString("N"));

            try
            {
                CreateRepositoryRootDirectoryAction.EnsureExists(path);

                Assert.True(Directory.Exists(path));
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        [Fact]
        public void EnsureExists_does_not_touch_an_existing_directorys_contents()
        {
            var path = Path.Combine(Path.GetTempPath(), "OutOfTheBox-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var markerFile = Path.Combine(path, "marker.txt");
            File.WriteAllText(markerFile, "preserved");

            try
            {
                CreateRepositoryRootDirectoryAction.EnsureExists(path);

                Assert.True(File.Exists(markerFile));
                Assert.Equal("preserved", File.ReadAllText(markerFile));
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        [Fact]
        public void GrantServiceAccountAccess_grants_full_control_on_the_root_itself()
        {
            var path = Path.Combine(Path.GetTempPath(), "OutOfTheBox-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            try
            {
                CreateRepositoryRootDirectoryAction.GrantServiceAccountAccess(path, TestAccountDomain, TestAccountName);

                Assert.True(HasFullControlRule(new DirectoryInfo(path).GetAccessControl()));
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        [Fact]
        public void GrantServiceAccountAccess_throws_when_the_account_does_not_exist()
        {
            // The exact real-machine bug this action's own retry logic guards against: SID
            // resolution for an account created moments earlier can transiently fail with
            // IdentityNotMappedException even though the account genuinely exists (a documented LSA
            // cache-lag issue, not a bug in this code) - GrantServiceAccountAccess retries a few
            // times before giving up. For a *genuinely* nonexistent account (this test), every retry
            // fails the same way, and the exception should still propagate once attempts are
            // exhausted - the internal overload's short retry policy keeps this test fast instead of
            // actually waiting out production's full SidResolveMaxAttempts x SidResolveRetryDelay.
            var path = Path.Combine(Path.GetTempPath(), "OutOfTheBox-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            try
            {
                Assert.ThrowsAny<IdentityNotMappedException>(
                    () => CreateRepositoryRootDirectoryAction.GrantServiceAccountAccess(
                        path, ".", "OutOfTheBox-Nonexistent-Account-" + Guid.NewGuid().ToString("N"),
                        maxAttempts: 2, retryDelay: TimeSpan.FromMilliseconds(10)));
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        [Fact]
        public void GrantServiceAccountAccess_reaches_pre_existing_subdirectories_and_files()
        {
            // The exact real-machine scenario this guards against: a repository that already
            // existed (cloned before this fix ran, or by a different identity entirely) - inherited
            // ACLs from a newly-added rule on the root never apply retroactively to it, so the grant
            // has to walk the existing tree explicitly, not just set-and-forget on the root.
            var path = Path.Combine(Path.GetTempPath(), "OutOfTheBox-Test-" + Guid.NewGuid().ToString("N"));
            var subdirectory = Path.Combine(path, "existing-repository", ".git", "objects", "pack");
            Directory.CreateDirectory(subdirectory);
            var packFile = Path.Combine(subdirectory, "pack-example.idx");
            File.WriteAllText(packFile, "pack data");

            try
            {
                CreateRepositoryRootDirectoryAction.GrantServiceAccountAccess(path, TestAccountDomain, TestAccountName);

                Assert.True(HasFullControlRule(new DirectoryInfo(subdirectory).GetAccessControl()));
                Assert.True(HasFullControlRule(new FileInfo(packFile).GetAccessControl()));
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private static bool HasFullControlRule(FileSystemSecurity security) =>
            security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(NTAccount))
                .Cast<FileSystemAccessRule>()
                .Any(rule =>
                    rule.IdentityReference.Value.Equals(CurrentIdentityName, StringComparison.OrdinalIgnoreCase) &&
                    rule.AccessControlType == AccessControlType.Allow &&
                    (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);
    }
}
