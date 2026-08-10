// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using System.Text;
using OutOfTheBox.Application.Execution;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// Captures both stdout and stderr from a single `git` invocation. Shared by
/// <see cref="RepositoryManager"/> (which needs stderr too, to report a failed action's error text)
/// and <see cref="GitRepositoryStatsProvider"/> (stdout only) - previously each had its own
/// near-identical private sink type.
/// </summary>
internal sealed class GitCaptureOutputSink : IProcessOutputSink
{
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();

    public string Stdout => _stdout.ToString();

    public string Stderr => _stderr.ToString();

    public Task OnStandardOutputAsync(string line, CancellationToken cancellationToken)
    {
        _stdout.AppendLine(line);
        return Task.CompletedTask;
    }

    public Task OnStandardErrorAsync(string line, CancellationToken cancellationToken)
    {
        _stderr.AppendLine(line);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Runs a short-lived `git` invocation and captures its stdout, for the ad-hoc lookups (branches,
/// remotes, status) both <see cref="RepositoryManager"/> and <see cref="GitRepositoryStatsProvider"/>
/// need outside the streamed/history-tracked run path - see those types for where each is called from.
/// </summary>
internal static class GitCaptureRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Runs <paramref name="arguments"/> as a `git` invocation and returns its captured stdout, or <see langword="null"/> on non-zero exit, cancellation, or the process failing to start.</summary>
    public static async Task<string?> CaptureAsync(
        IProcessRunner processRunner,
        ILogger logger,
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var sink = new GitCaptureOutputSink();

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
            // environment doesn't resolve it) - treated the same as "this invocation produced
            // nothing usable" rather than allowed to propagate. Left uncaught here, this previously
            // escaped GitRepositoryStatsProvider's RecomputeOneAsync/RecomputeAllAsync (which only
            // caught OperationCanceledException) and crashed RepositoryStatsSampler's
            // BackgroundService - which by default (BackgroundServiceExceptionBehavior.StopHost)
            // stops the whole host, silently ending every future stats update, not just this one
            // repository's. Logged (not just swallowed) because that exact "stats silently stop
            // updating" bug was previously diagnosable only by reading code, not logs - a repeat
            // occurrence should be visible to the operator, not invisible.
            logger.LogWarning(ex, "git {Arguments} failed to start in {WorkingDirectory}.", string.Join(' ', arguments), workingDirectory);
            return null;
        }
    }
}
