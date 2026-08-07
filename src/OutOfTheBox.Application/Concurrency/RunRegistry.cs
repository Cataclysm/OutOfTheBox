// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;

namespace OutOfTheBox.Application.Concurrency;

/// <summary>
/// Tracks which repos currently have an in-flight run, enforcing at most one <c>dotnet</c> command
/// per repo at a time while allowing distinct repos to run fully in parallel (per
/// specs/dotnet-command-execution's "Commands against different repos run in parallel" and
/// "One in-flight command per repo" requirements), and lets a run be looked up and cancelled by
/// its run id (per the "Caller can cancel an in-flight command" requirement). Registered as a
/// singleton - this is process-wide, in-memory, and lost on restart by design (see design.md's Risks).
/// </summary>
public sealed class RunRegistry
{
    private readonly ConcurrentDictionary<string, RunHandle> _activeRunsByRepoRoot = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, RunHandle> _activeRunsByRunId = new();

    /// <summary>
    /// Attempts to acquire the lock for <paramref name="repoRoot"/> (the exact resolved working
    /// directory of the request, not a higher-level grouping) on behalf of <paramref name="runId"/>.
    /// Atomic: under concurrent callers targeting the same repo, exactly one succeeds.
    /// </summary>
    /// <param name="repoRoot">The resolved working directory to lock.</param>
    /// <param name="runId">The run id requesting the lock.</param>
    /// <param name="cancellationTokenSource">
    /// The source a later <see cref="TryCancel"/> call for this run id will cancel. Ownership
    /// stays with the caller - this registry never disposes it.
    /// </param>
    /// <param name="conflictingRunId">
    /// When this method returns <see langword="false"/>, the run id already holding the lock;
    /// otherwise <see langword="default"/>.
    /// </param>
    public bool TryAcquire(string repoRoot, Guid runId, CancellationTokenSource cancellationTokenSource, out Guid conflictingRunId)
    {
        var handle = new RunHandle(runId, cancellationTokenSource);

        if (!_activeRunsByRepoRoot.TryAdd(repoRoot, handle))
        {
            conflictingRunId = _activeRunsByRepoRoot[repoRoot].RunId;
            return false;
        }

        _activeRunsByRunId[runId] = handle;
        conflictingRunId = default;
        return true;
    }

    /// <summary>Releases the lock for <paramref name="repoRoot"/>, so a subsequent request for it can be accepted.</summary>
    public void Release(string repoRoot)
    {
        if (_activeRunsByRepoRoot.TryRemove(repoRoot, out var handle))
        {
            _activeRunsByRunId.TryRemove(handle.RunId, out _);
        }
    }

    /// <summary>
    /// Requests cancellation of the run identified by <paramref name="runId"/>. Returns
    /// <see langword="false"/> - with no side effects - if the run id is unknown or has already
    /// reached a terminal state, matching specs/dotnet-command-execution's "Cancelling an unknown
    /// or finished run" scenario.
    /// </summary>
    public bool TryCancel(Guid runId)
    {
        if (!_activeRunsByRunId.TryGetValue(runId, out var handle))
        {
            return false;
        }

        try
        {
            handle.CancellationTokenSource.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The run reached a terminal state and its CancellationTokenSource was disposed
            // between our lookup above and this Cancel() call - a benign race. Treat it exactly
            // like "run id not found": there is nothing left to cancel.
            return false;
        }
    }

    private sealed record RunHandle(Guid RunId, CancellationTokenSource CancellationTokenSource);
}
