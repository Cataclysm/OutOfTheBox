// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Runtime.CompilerServices;

namespace OutOfTheBox.BehaviorTests.Support;

/// <summary>
/// Points every git invocation this test process spawns at a throwaway, file-based
/// <c>credential.helper</c> before any test runs - the same "set exactly once, at assembly load,
/// not per factory" reasoning <see cref="TestDataDirectorySetup"/> already established for
/// <c>OUTOFTHEBOX_DATA_DIR</c> (a child <c>git.exe</c> process inherits this test process's own
/// environment variables, which are raw process-wide mutable state; setting this once before any
/// test - including several running concurrently, which this suite does for real - avoids a
/// last-writer-wins race and, more importantly here, ensures `authorize_git_host`/`git credential
/// approve` scenarios never touch the real developer/CI machine's actual global git config or
/// Windows Credential Manager). <c>GIT_CONFIG_GLOBAL</c> (git 2.32+) overrides which file git reads
/// as "the" global config, without needing to relocate <c>HOME</c>/<c>USERPROFILE</c> (which other,
/// unrelated tooling might depend on). <c>credential.helper = store --file=...</c> is git's own
/// built-in, plain-text, file-based helper - never Windows Credential Manager - so a test-authorized
/// "PAT" (always a throwaway string in these scenarios) never reaches real OS-level credential
/// storage.
/// </summary>
internal static class TestGitCredentialConfigSetup
{
    [ModuleInitializer]
    internal static void ConfigureTestGitCredentialHelper()
    {
        var configDirectory = Path.Combine(Path.GetTempPath(), $"OutOfTheBox-BehaviorTests-GitConfig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDirectory);

        var credentialStorePath = Path.Combine(configDirectory, "credentials");
        var globalConfigPath = Path.Combine(configDirectory, ".gitconfig");
        File.WriteAllText(globalConfigPath, $"[credential]\n\thelper = store --file={credentialStorePath.Replace('\\', '/')}\n");

        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", globalConfigPath);
    }
}
