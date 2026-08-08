// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace OutOfTheBox.Msi.CustomActions.Tests
{
    /// <summary>
    /// Exercises the real <c>git config --global</c> (not <c>--system</c>, which needs elevation
    /// this test process doesn't have) - the same "real external tool, not mocked" precedent
    /// <c>CertificateGeneratorTests</c>/the main solution's <c>InstalledToolVersionsProviderTests</c>
    /// already apply, since git is guaranteed present in any environment that can build/test this
    /// repository. Each test uses its own random-GUID pattern and unsets it afterward, so a test
    /// failure can never leave a stray entry in the machine's real global gitconfig.
    /// </summary>
    public class ConfigureGitSafeDirectoryTests
    {
        [Theory]
        [InlineData(@"C:\repositories", "C:/repositories/*")]
        [InlineData(@"C:\repositories\", "C:/repositories/*")]
        [InlineData("C:/repositories", "C:/repositories/*")]
        public void BuildSafeDirectoryPattern_normalizes_slashes_and_appends_wildcard(string repositoryRootDirectory, string expected) =>
            Assert.Equal(expected, ConfigureGitSafeDirectoryAction.BuildSafeDirectoryPattern(repositoryRootDirectory));

        [Fact]
        public void EnsureSafeDirectory_adds_the_pattern_when_not_already_present()
        {
            var pattern = UniquePattern();

            try
            {
                ConfigureGitSafeDirectoryAction.EnsureSafeDirectory(pattern, "--global");

                Assert.Contains(pattern, GetAllSafeDirectories());
            }
            finally
            {
                Unset(pattern);
            }
        }

        [Fact]
        public void EnsureSafeDirectory_does_not_duplicate_an_already_present_entry()
        {
            var pattern = UniquePattern();

            try
            {
                ConfigureGitSafeDirectoryAction.EnsureSafeDirectory(pattern, "--global");
                ConfigureGitSafeDirectoryAction.EnsureSafeDirectory(pattern, "--global");

                Assert.Single(GetAllSafeDirectories(), entry => entry == pattern);
            }
            finally
            {
                Unset(pattern);
            }
        }

        private static string UniquePattern() => $"C:/OutOfTheBox-Test-{Guid.NewGuid():N}/*";

        private static string[] GetAllSafeDirectories() =>
            RunGit("config", "--global", "--get-all", "safe.directory")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        private static void Unset(string pattern) => RunGit("config", "--global", "--unset-all", "safe.directory", pattern);

        private static string RunGit(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                // net472 has no ProcessStartInfo.ArgumentList - see ConfigureGitSafeDirectory.cs's
                // own copy of this same reasoning.
                Arguments = string.Join(" ", arguments.Select(a => a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"")),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = Process.Start(startInfo))
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }
    }
}
