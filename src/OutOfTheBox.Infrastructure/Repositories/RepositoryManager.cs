// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IRepositoryManager" />
/// <remarks>
/// Registered scoped (it depends on the scoped <see cref="IRunRepository"/>), called directly from
/// Blazor component code-behind - never through an HTTP endpoint, per specs/repository-management's
/// "reachable only from the authenticated dashboard" requirement. <see cref="CloneAsync"/>'s
/// background continuation resolves its own <see cref="IRunRepository"/> from a fresh
/// <see cref="IServiceScopeFactory"/>-created scope rather than closing over the caller's injected
/// instance - the calling Blazor circuit's scope (and its <c>DbContext</c>) may not outlive the
/// clone, which keeps running after <see cref="CloneAsync"/> itself has already returned.
/// </remarks>
public sealed class RepositoryManager(
    IWorkingDirectoryResolver workingDirectoryResolver,
    RunRegistry runRegistry,
    IRunRepository runRepository,
    IRunEventBus runEventBus,
    IProcessRunner processRunner,
    IRepositoryStatsProvider statsProvider,
    RepositoryStatsCache statsCache,
    IRepositoryStatsEventBus statsEventBus,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<ServiceOptions> options) : IRepositoryManager
{
    /// <inheritdoc />
    public Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken cancellationToken)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<RepositorySummary>>([]);
        }

        var summaries = new List<RepositorySummary>();

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            var resolvedPath = Path.GetFullPath(directory);
            var stats = statsCache.TryGet(name);
            var isActive = runRegistry.IsHeld(resolvedPath);

            summaries.Add(stats is null
                ? new RepositorySummary { Name = name, Path = resolvedPath, StatsComputed = false, IsActive = isActive }
                : new RepositorySummary
                {
                    Name = name,
                    Path = resolvedPath,
                    StatsComputed = true,
                    TotalSizeBytes = stats.TotalSizeBytes,
                    IsGitRepository = stats.IsGitRepository,
                    Branch = stats.Branch,
                    IsDirty = stats.IsDirty,
                    AheadCount = stats.AheadCount,
                    BehindCount = stats.BehindCount,
                    IsActive = isActive,
                });
        }

        return Task.FromResult<IReadOnlyList<RepositorySummary>>(summaries);
    }

    /// <inheritdoc />
    public async Task<RepositoryActionResult> CloneAsync(string url, string name, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var targetPath = resolution.ResolvedPath!;

        if (Directory.Exists(targetPath))
        {
            var now = DateTimeOffset.UtcNow;
            await runRepository.AddAsync(new Run
            {
                Id = Guid.NewGuid(),
                Kind = RunKind.RepositoryClone,
                RepositoryPath = targetPath,
                SourceUrl = url,
                StartedAt = now,
                CompletedAt = now,
                Outcome = RunOutcome.AlreadyExists,
            }, cancellationToken);

            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.AlreadyExists);
        }

        var runId = Guid.NewGuid();
        var cancelRequestCts = new CancellationTokenSource();

        if (!runRegistry.TryAcquire(targetPath, runId, cancelRequestCts, out var conflictingRunId))
        {
            cancelRequestCts.Dispose();
            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
        }

        var run = new Run
        {
            Id = runId,
            Kind = RunKind.RepositoryClone,
            RepositoryPath = targetPath,
            SourceUrl = url,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, cancellationToken);
        runEventBus.Publish(new RunEvent(runId, RunKind.RepositoryClone, RunEventType.Started, targetPath));

        // Fire-and-forget: the operator's click shouldn't block for the clone's full duration - it
        // returns as soon as the run is accepted and keeps going in the background, visible via
        // Status/History like any other run, per specs/service-dashboard's "the clone starts,
        // appears as an in-flight run" requirement.
        _ = RunCloneToCompletionAsync(run, targetPath, url, cancelRequestCts);

        return new RepositoryActionResult.Accepted(runId);
    }

    /// <inheritdoc />
    public async Task<RepositoryActionResult> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var targetPath = resolution.ResolvedPath!;

        if (!Directory.Exists(targetPath))
        {
            var now = DateTimeOffset.UtcNow;
            await runRepository.AddAsync(new Run
            {
                Id = Guid.NewGuid(),
                Kind = RunKind.RepositoryDelete,
                RepositoryPath = targetPath,
                StartedAt = now,
                CompletedAt = now,
                Outcome = RunOutcome.NotFound,
            }, cancellationToken);

            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.NotFound);
        }

        var runId = Guid.NewGuid();
        var cancelRequestCts = new CancellationTokenSource();

        // Acquire, not just check-then-act: closes the TOCTOU race a plain existence/lock check
        // would leave open between "confirmed idle" and "delete actually ran," per design.md.
        if (!runRegistry.TryAcquire(targetPath, runId, cancelRequestCts, out var conflictingRunId))
        {
            cancelRequestCts.Dispose();
            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
        }

        var run = new Run
        {
            Id = runId,
            Kind = RunKind.RepositoryDelete,
            RepositoryPath = targetPath,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, cancellationToken);
        runEventBus.Publish(new RunEvent(runId, RunKind.RepositoryDelete, RunEventType.Started, targetPath));

        try
        {
            // Directory.Delete(recursive: true) throws UnauthorizedAccessException on Windows for
            // any read-only file in the tree instead of just deleting it - a real, common case for
            // a git checkout (git itself sometimes leaves pack/object files read-only), found on
            // real-machine use: deletion kept failing (silently, before the catch below existed)
            // even though nothing else held the directory open. Clearing the attribute first is the
            // standard workaround; a genuinely locked file (open elsewhere) still fails below, now
            // with the actual reason captured instead of just a bare "it failed."
            ClearReadOnlyAttributes(targetPath);
            Directory.Delete(targetPath, recursive: true);
            run.Outcome = RunOutcome.Completed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked file (still open in another process) or a permission problem the read-only
            // clear above didn't cover - found on real-machine use: without this catch,
            // Directory.Delete's exception propagated straight out of this method (only a `finally`
            // existed, no `catch`), which still ran and set CompletedAt but never reached the line
            // above that sets Outcome, leaving the persisted row in the contradictory
            // Running-but-completed state that surfaced the bug - and nothing was actually deleted,
            // silently. The exception's own message is captured into Stderr so an operator can see
            // *why* it failed, not just that it did.
            run.Outcome = RunOutcome.Failed;
            run.Stderr = ex.Message;
        }
        finally
        {
            run.CompletedAt = DateTimeOffset.UtcNow;
            await runRepository.UpdateAsync(run, CancellationToken.None);
            runEventBus.Publish(new RunEvent(runId, RunKind.RepositoryDelete, RunEventType.Terminal, targetPath));

            statsCache.Remove(name);
            runRegistry.Release(targetPath);
            cancelRequestCts.Dispose();
        }

        return new RepositoryActionResult.Accepted(runId);
    }

    /// <summary>
    /// Recursively clears the read-only attribute from every file under <paramref name="path"/> -
    /// see <see cref="DeleteAsync"/>'s own remarks for why this is necessary before a recursive delete.
    /// </summary>
    private static void ClearReadOnlyAttributes(string path)
    {
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
    }

    private async Task RunCloneToCompletionAsync(Run run, string targetPath, string url, CancellationTokenSource cancelRequestCts)
    {
        var sink = new RepositoryCloneOutputSink(runEventBus, run.Id, targetPath, options.Value.OutputCapBytes);

        try
        {
            var timeout = TimeSpan.FromSeconds(options.Value.DefaultExecutionTimeoutSeconds);
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancelRequestCts.Token);

            try
            {
                var result = await processRunner.RunAsync(
                    new ProcessRunRequest(["clone", url, targetPath], options.Value.RootDirectory, "git"),
                    sink,
                    linkedCts.Token,
                    onStarted: pid => runRegistry.SetProcessId(run.Id, pid));

                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Outcome = RunOutcome.Completed;
                run.ExitCode = result.ExitCode;
                run.Stdout = sink.Stdout;
                run.Stderr = sink.Stderr;
                run.Truncated = sink.Truncated;
            }
            catch (OperationCanceledException)
            {
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Stdout = sink.Stdout;
                run.Stderr = sink.Stderr;
                run.Truncated = sink.Truncated;
                run.Outcome = cancelRequestCts.IsCancellationRequested ? RunOutcome.Cancelled : RunOutcome.TimedOut;
            }
            catch (Win32Exception ex)
            {
                // git.exe failing to even start (missing/corrupted) - same class of "operation
                // attempted but failed outside caller control" as RepositoryManager.DeleteAsync's
                // IOException/UnauthorizedAccessException catch. Without this, the exception would
                // propagate out of this background continuation entirely, skipping the
                // UpdateAsync call below and leaving the run parked at Running forever - this
                // method has no HTTP response to abort onto, so there's no other signal that
                // would ever surface the failure. The message is captured into Stderr the same way
                // DeleteAsync's catch does, so an operator sees why, not just that it failed.
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Outcome = RunOutcome.Failed;
                run.Stderr = ex.Message;
            }

            // A fresh scope/DbContext, not the caller's - the Blazor circuit that started this
            // clone may already be gone by the time it finishes.
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var scopedRunRepository = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            await scopedRunRepository.UpdateAsync(run, CancellationToken.None);

            // Computed and cached BEFORE the Terminal event below, not after - found on real-machine
            // use: Repositories.razor refreshes exactly once, synchronously, off that event, so
            // publishing it first left the dashboard's one-shot snapshot racing this computation and
            // permanently stuck showing "Computing…" (StatsComputed still false at snapshot time)
            // until some unrelated event elsewhere triggered another refresh. Ordering this first
            // means the snapshot the Terminal event triggers always already has the fresh stats.
            if (run.Outcome == RunOutcome.Completed)
            {
                var stats = await statsProvider.ComputeAsync(targetPath, CancellationToken.None);
                var name = Path.GetFileName(targetPath);
                statsCache.Set(name, stats);
                statsEventBus.Publish(name);
            }

            runEventBus.Publish(new RunEvent(run.Id, RunKind.RepositoryClone, RunEventType.Terminal, targetPath));
        }
        finally
        {
            runRegistry.Release(targetPath);
            cancelRequestCts.Dispose();
        }
    }
}
