// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IRepositoryStatsProvider" />
/// <remarks>
/// Reuses <see cref="IProcessRunner"/>'s process-spawning mechanics (per task 13.3) for the
/// internal git invocations, but never goes through <c>RunEndpoints</c>/SSE/<c>RunRegistry</c>/
/// history - this is background telemetry sampling, not an operator-triggered run, so its output
/// is captured into a plain string via a throwaway <see cref="IProcessOutputSink"/>, not streamed
/// or persisted anywhere.
/// </remarks>
public sealed class GitRepositoryStatsProvider(IProcessRunner processRunner) : IRepositoryStatsProvider
{
    private static readonly TimeSpan GitInvocationTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task<RepositoryStats> ComputeAsync(string repoPath, CancellationToken cancellationToken)
    {
        var totalSizeBytes = ComputeDirectorySize(repoPath);

        if (!Directory.Exists(Path.Combine(repoPath, ".git")))
        {
            return new RepositoryStats(totalSizeBytes, IsGitRepository: false, Branch: null, IsDirty: false, AheadCount: null, BehindCount: null);
        }

        var branch = (await RunGitCaptureAsync(repoPath, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken))?.Trim();
        var statusOutput = await RunGitCaptureAsync(repoPath, ["status", "--porcelain"], cancellationToken);
        var isDirty = !string.IsNullOrEmpty(statusOutput?.Trim());

        int? ahead = null;
        int? behind = null;

        // Fails (non-zero exit) when no upstream is configured - treated as "no upstream" rather
        // than an error, per specs/repository-management's "ahead/behind its upstream if one is
        // configured" wording.
        var aheadBehind = await RunGitCaptureAsync(repoPath, ["rev-list", "--left-right", "--count", "@{upstream}...HEAD"], cancellationToken);
        if (aheadBehind is not null)
        {
            var parts = aheadBehind.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var behindCount) && int.TryParse(parts[1], out var aheadCount))
            {
                behind = behindCount;
                ahead = aheadCount;
            }
        }

        return new RepositoryStats(totalSizeBytes, IsGitRepository: true, branch, isDirty, ahead, behind);
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
