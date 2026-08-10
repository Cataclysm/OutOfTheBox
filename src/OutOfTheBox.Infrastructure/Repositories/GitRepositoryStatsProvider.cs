// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IRepositoryStatsProvider" />
/// <remarks>
/// Reuses <see cref="IProcessRunner"/>'s process-spawning mechanics (per task 13.3) for the
/// internal git invocations, but never goes through <c>RunRegistry</c>/history - this is background
/// telemetry sampling, not an operator-triggered run, so its output is captured into a plain string
/// via <see cref="GitCaptureRunner"/>, not streamed or persisted anywhere.
/// </remarks>
public sealed class GitRepositoryStatsProvider(IProcessRunner processRunner, IGitCredentialStore gitCredentialStore, ILogger<GitRepositoryStatsProvider> logger) : IRepositoryStatsProvider
{
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
        var symbolicRef = (await GitCaptureRunner.CaptureAsync(processRunner, logger, repositoryPath, ["symbolic-ref", "-q", "--short", "HEAD"], cancellationToken))?.Trim();
        var isDetachedHead = symbolicRef is null;
        var branch = isDetachedHead
            ? (await GitCaptureRunner.CaptureAsync(processRunner, logger, repositoryPath, ["rev-parse", "--short", "HEAD"], cancellationToken))?.Trim()
            : symbolicRef;

        var statusOutput = await GitCaptureRunner.CaptureAsync(processRunner, logger, repositoryPath, ["status", "--porcelain"], cancellationToken);
        var isDirty = !string.IsNullOrEmpty(statusOutput?.Trim());

        int? ahead = null;
        int? behind = null;

        // Fails (non-zero exit) when no upstream is configured - treated as "no upstream" rather
        // than an error, per specs/repository-management's "ahead/behind its upstream if one is
        // configured" wording.
        var aheadBehind = await GitCaptureRunner.CaptureAsync(processRunner, logger, repositoryPath, ["rev-list", "--left-right", "--count", "@{upstream}...HEAD"], cancellationToken);
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
            var trackState = await GitCaptureRunner.CaptureAsync(processRunner, logger, repositoryPath, ["for-each-ref", "--format=%(upstream:track)", $"refs/heads/{branch}"], cancellationToken);
            isRemoteGone = trackState?.Contains("[gone]", StringComparison.Ordinal) == true;
        }

        var remotes = await ComputeRemotesAsync(repositoryPath, cancellationToken);
        var needsCredential = await ComputeNeedsCredentialAsync(remotes, cancellationToken);

        return new GitStatusSnapshot(IsGitRepository: true, branch, isDirty, ahead, behind, isRemoteGone, remotes, isDetachedHead, needsCredential);
    }

    /// <summary>
    /// Resolves this repository's <c>origin</c> remote to a host and looks up its recorded
    /// credential health - folded into this same sampling pass since <paramref name="remotes"/> is
    /// already fetched on every pass for the detail page's remote list, so deriving the host costs
    /// nothing extra (per design.md's "computed into the existing repository-stats pass" decision).
    /// A repository with no <c>origin</c> remote, or one whose URL doesn't resolve to a host, never
    /// needs a credential (nothing to check against).
    /// </summary>
    private async Task<bool> ComputeNeedsCredentialAsync(IReadOnlyList<Application.Repositories.RepositoryRemote> remotes, CancellationToken cancellationToken)
    {
        var origin = remotes.FirstOrDefault(r => r.Name == "origin");
        if (origin is null || !GitRemoteUrlParser.TryGetHost(origin.Url, out var host))
        {
            return false;
        }

        var health = await gitCredentialStore.GetHealthAsync(host, cancellationToken);
        return GitHostCredentialHealth.NeedsCredential(health);
    }

    private async Task<IReadOnlyList<Application.Repositories.RepositoryRemote>> ComputeRemotesAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var output = await GitCaptureRunner.CaptureAsync(processRunner, logger, repositoryPath, ["remote", "-v"], cancellationToken);
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        // "origin\thttps://example.com/repo.git (fetch)" / "(push)" - one line per remote per
        // direction; de-duplicated to one entry per remote name, keeping whichever URL is seen first
        // (fetch and push URLs are almost always identical, and this is a display summary, not a
        // config editor).
        var remotes = new List<Application.Repositories.RepositoryRemote>();
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

            remotes.Add(new Application.Repositories.RepositoryRemote(name, urlAndDirection[0]));
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
}
