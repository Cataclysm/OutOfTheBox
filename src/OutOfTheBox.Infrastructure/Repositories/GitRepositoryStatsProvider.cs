// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using System.Text;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IRepositoryStatsProvider" />
/// <remarks>
/// Reuses <see cref="IProcessRunner"/>'s process-spawning mechanics (per task 13.3) for the
/// internal git invocations, but never goes through <c>RunEndpoints</c>/SSE/<c>RunRegistry</c>/
/// history - this is background telemetry sampling, not an operator-triggered run, so its output
/// is captured into a plain string via a throwaway <see cref="IProcessOutputSink"/>, not streamed
/// or persisted anywhere.
/// </remarks>
public sealed class GitRepositoryStatsProvider(IProcessRunner processRunner, ILogger<GitRepositoryStatsProvider> logger) : IRepositoryStatsProvider
{
    private static readonly TimeSpan GitInvocationTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task<RepositoryStats> ComputeAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var totalSizeBytes = await ComputeSizeAsync(repositoryPath, cancellationToken);
        var gitStatus = await ComputeGitStatusAsync(repositoryPath, cancellationToken);

        return new RepositoryStats(
            totalSizeBytes,
            gitStatus.IsGitRepository,
            gitStatus.Branch,
            gitStatus.IsDirty,
            gitStatus.AheadCount,
            gitStatus.BehindCount,
            gitStatus.IsRemoteGone,
            gitStatus.Remotes,
            gitStatus.IsDetachedHead);
    }

    /// <inheritdoc />
    public Task<long> ComputeSizeAsync(string repositoryPath, CancellationToken cancellationToken) =>
        Task.FromResult(ComputeDirectorySize(repositoryPath));

    /// <inheritdoc />
    public async Task<GitStatusSnapshot> ComputeGitStatusAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(repositoryPath, ".git")))
        {
            return new GitStatusSnapshot(IsGitRepository: false, Branch: null, IsDirty: false, AheadCount: null, BehindCount: null, IsRemoteGone: false, Remotes: []);
        }

        // `git symbolic-ref -q --short HEAD` succeeds (printing the branch name) only when HEAD is
        // attached to a branch; it fails when detached, which is how a detached HEAD is
        // distinguished here - `git rev-parse --abbrev-ref HEAD` (the previous approach) instead
        // returns the literal string "HEAD" when detached, which was previously passed straight
        // through and displayed as if "HEAD" were a real branch name.
        var symbolicRef = (await RunGitCaptureAsync(repositoryPath, ["symbolic-ref", "-q", "--short", "HEAD"], cancellationToken))?.Trim();
        var isDetachedHead = symbolicRef is null;
        var branch = isDetachedHead
            ? (await RunGitCaptureAsync(repositoryPath, ["rev-parse", "--short", "HEAD"], cancellationToken))?.Trim()
            : symbolicRef;

        var statusOutput = await RunGitCaptureAsync(repositoryPath, ["status", "--porcelain"], cancellationToken);
        var isDirty = !string.IsNullOrEmpty(statusOutput?.Trim());

        int? ahead = null;
        int? behind = null;

        // Fails (non-zero exit) when no upstream is configured - treated as "no upstream" rather
        // than an error, per specs/repository-management's "ahead/behind its upstream if one is
        // configured" wording.
        var aheadBehind = await RunGitCaptureAsync(repositoryPath, ["rev-list", "--left-right", "--count", "@{upstream}...HEAD"], cancellationToken);
        if (aheadBehind is not null)
        {
            var parts = aheadBehind.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var behindCount) && int.TryParse(parts[1], out var aheadCount))
            {
                behind = behindCount;
                ahead = aheadCount;
            }
        }

        // Distinguishes "upstream configured but its remote-side branch was deleted" from "no
        // upstream configured at all" - both collapse to a bare rev-list failure above (ahead/behind
        // stay null either way), so this is git's own tracking-state marker, which reports the
        // literal "[gone]" token when the upstream ref it remembers no longer exists on the remote.
        var isRemoteGone = false;
        if (!isDetachedHead && !string.IsNullOrEmpty(branch))
        {
            var trackState = await RunGitCaptureAsync(repositoryPath, ["for-each-ref", "--format=%(upstream:track)", $"refs/heads/{branch}"], cancellationToken);
            isRemoteGone = trackState?.Contains("[gone]", StringComparison.Ordinal) == true;
        }

        var remotes = await ComputeRemotesAsync(repositoryPath, cancellationToken);

        return new GitStatusSnapshot(IsGitRepository: true, branch, isDirty, ahead, behind, isRemoteGone, remotes, isDetachedHead);
    }

    private async Task<IReadOnlyList<RepositoryRemote>> ComputeRemotesAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var output = await RunGitCaptureAsync(repositoryPath, ["remote", "-v"], cancellationToken);
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        // "origin\thttps://example.com/repo.git (fetch)" / "(push)" - one line per remote per
        // direction; de-duplicated to one entry per remote name, keeping whichever URL is seen first
        // (fetch and push URLs are almost always identical, and this is a display summary, not a
        // config editor).
        var remotes = new List<RepositoryRemote>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('\t', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var name = parts[0];
            var urlAndDirection = parts[1].Split(' ', 2);
            if (urlAndDirection.Length == 0 || !seenNames.Add(name))
            {
                continue;
            }

            remotes.Add(new RepositoryRemote(name, urlAndDirection[0]));
        }

        return remotes;
    }

    private static long ComputeDirectorySize(string path)
    {
        long total = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // A file deleted/moved mid-enumeration (e.g. a build running concurrently) -
                    // skip it rather than fail the whole size computation for one transient file.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return total;
    }

    private async Task<string?> RunGitCaptureAsync(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(GitInvocationTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        var sink = new CollectingOutputSink();

        try
        {
            var result = await processRunner.RunAsync(new ProcessRunRequest(arguments, workingDirectory, "git"), sink, linkedCts.Token);
            return result.ExitCode == 0 ? sink.Stdout : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Win32Exception ex)
        {
            // git.exe unreachable for the process's account/PATH (e.g. a service account whose
            // environment doesn't resolve it) - treated the same as "this git invocation produced
            // nothing usable" rather than allowed to propagate. Left uncaught, this previously
            // escaped RecomputeOneAsync/RecomputeAllAsync (which only caught
            // OperationCanceledException) and crashed RepositoryStatsSampler's BackgroundService -
            // which by default (BackgroundServiceExceptionBehavior.StopHost) stops the whole host,
            // silently ending every future stats update, not just this one repository's. Logged
            // (not just swallowed) because this exact "stats silently stop updating" bug was
            // previously diagnosable only by reading code, not logs - a repeat occurrence (this
            // repository or a different one) should be visible to the operator, not invisible.
            logger.LogWarning(ex, "git {Arguments} failed to start in {WorkingDirectory} while sampling repository stats.", string.Join(' ', arguments), workingDirectory);
            return null;
        }
    }

    private sealed class CollectingOutputSink : IProcessOutputSink
    {
        private readonly StringBuilder _stdout = new();

        public string Stdout => _stdout.ToString();

        public Task OnStandardOutputAsync(string line, CancellationToken cancellationToken)
        {
            _stdout.AppendLine(line);
            return Task.CompletedTask;
        }

        public Task OnStandardErrorAsync(string line, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
