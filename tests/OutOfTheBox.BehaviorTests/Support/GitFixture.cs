// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Diagnostics;

namespace OutOfTheBox.BehaviorTests.Support;

/// <summary>
/// A real, working git repository materialized fresh in a temp directory for
/// <c>git-command-execution</c> BDD scenarios to run real <c>git.exe</c> commands against.
/// </summary>
/// <remarks>
/// Unlike <c>PassingFixture</c>/<c>FailingFixture</c>/<c>HangingFixture</c>, this fixture is
/// **not** checked into the repository as static content: a real git repository (a <c>.git</c>
/// directory with real refs/objects) can't cleanly nest inside this project's own git history -
/// it would either be ignored or treated as a submodule gitlink depending on how it was added,
/// neither of which is what's wanted here. Generating a fresh repository per test run also sidesteps a
/// second problem: git commands genuinely mutate a working tree (<c>reset --hard</c>,
/// <c>clean</c>, ...), so a single checked-in fixture repository would accumulate mutations across test
/// runs and stop being deterministic. <see cref="CreateAsync"/> builds an isolated, disposable
/// repository instead, the same way <c>WorkingDirectoryResolverTests</c> already builds disposable real
/// temp-directory trees for its own scenarios.
/// </remarks>
public sealed class GitFixture : IDisposable
{
    private const string RepositoryName = "GitFixture";

    private GitFixture(string rootDirectory) => RootDirectory = rootDirectory;

    /// <summary>
    /// The directory to point <c>OutOfTheBox:RootDirectory</c> at - it contains one subdirectory,
    /// <c>GitFixture</c>, matching the <c>workingDirectory: "GitFixture"</c> convention the other
    /// checked-in fixtures already use.
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// Creates a fresh temp directory, runs <c>git init</c>, configures a local commit identity
    /// (a brand-new repository has none), commits one file, and - if <paramref name="withBlockingHook"/>
    /// is set - installs a <c>pre-commit</c> hook that blocks indefinitely (<c>ping -t</c>, killed
    /// only by process termination) so a scenario needing a genuinely long-running, cancellable
    /// git command has one: <c>git commit --allow-empty -m "..."</c> against a fixture built this
    /// way hangs exactly the way <c>HangingFixture</c>'s <c>Task.Delay(Timeout.Infinite)</c> does
    /// for <c>dotnet</c> scenarios, for the same reason (git has no built-in command that simply
    /// never returns).
    /// </summary>
    public static async Task<GitFixture> CreateAsync(bool withBlockingHook = false)
    {
        var root = Directory.CreateTempSubdirectory("OutOfTheBox.GitFixture.").FullName;
        var repositoryPath = Path.Combine(root, RepositoryName);
        Directory.CreateDirectory(repositoryPath);

        await RunGitAsync(repositoryPath, "init", "-q");
        await RunGitAsync(repositoryPath, "config", "user.email", "fixture@example.com");
        await RunGitAsync(repositoryPath, "config", "user.name", "OutOfTheBox Fixture");

        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "README.md"), "GitFixture\n");
        await RunGitAsync(repositoryPath, "add", "-A");
        await RunGitAsync(repositoryPath, "commit", "-q", "-m", "initial commit");

        if (withBlockingHook)
        {
            var hooksDir = Path.Combine(repositoryPath, ".git", "hooks");
            var hookPath = Path.Combine(hooksDir, "pre-commit");
            await File.WriteAllTextAsync(hookPath, "#!/bin/sh\nping -t 127.0.0.1 >/dev/null\n");
        }

        return new GitFixture(root);
    }

    // internal, not private - RepositoryManagementSteps reuses this for its own ad-hoc test-setup
    // commits (e.g. a second commit to exercise parent-info lookup) rather than duplicating a second
    // near-identical raw git-process runner.
    internal static async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {stderr}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup - a lingering handle (e.g. a not-yet-fully-torn-down hung
            // process from a cancellation scenario) shouldn't fail the test.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above.
        }
    }
}
