// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Collections.Concurrent;

namespace OutOfTheBox.Application.Concurrency;

/// <summary>
/// Tracks which repositories currently have an in-flight run, enforcing at most one <c>dotnet</c> command
/// per repository at a time while allowing distinct repositories to run fully in parallel (per
/// specs/dotnet-command-execution's "Commands against different repositories run in parallel" and
/// "One in-flight command per repository" requirements), and lets a run be looked up and cancelled by
/// its run id (per the "Caller can cancel an in-flight command" requirement). Registered as a
/// singleton - this is process-wide, in-memory, and lost on restart by design (see design.md's Risks).
/// </summary>
public sealed class RunRegistry
{
    private readonly ConcurrentDictionary<string, RunHandle> _activeRunsByRepositoryRoot = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, RunHandle> _activeRunsByRunId = new();

    /// <summary>
    /// Attempts to acquire the lock for <paramref name="repositoryRoot"/> (the exact resolved working
    /// directory of the request, not a higher-level grouping) on behalf of <paramref name="runId"/>.
    /// Atomic: under concurrent callers targeting the same repository, exactly one succeeds.
    /// </summary>
    /// <param name="repositoryRoot">The resolved working directory to lock.</param>
    /// <param name="runId">The run id requesting the lock.</param>
    /// <param name="cancellationTokenSource">
    /// The source a later <see cref="TryCancel"/> call for this run id will cancel. Ownership
    /// stays with the caller - this registry never disposes it.
    /// </param>
    /// <param name="conflictingRunId">
    /// When this method returns <see langword="false"/>, the run id already holding the lock;
    /// otherwise <see langword="default"/>.
    /// </param>
    public bool TryAcquire(string repositoryRoot, Guid runId, CancellationTokenSource cancellationTokenSource, out Guid conflictingRunId)
    {
        var handle = new RunHandle(runId, cancellationTokenSource);

        if (!_activeRunsByRepositoryRoot.TryAdd(repositoryRoot, handle))
        {
            conflictingRunId = _activeRunsByRepositoryRoot[repositoryRoot].RunId;
            return false;
        }

        _activeRunsByRunId[runId] = handle;
        conflictingRunId = default;
        return true;
    }

    /// <summary>
    /// Read-only check of whether <paramref name="repositoryRoot"/> currently holds the per-repository lock -
    /// no side effect, unlike <see cref="TryAcquire"/>. Used for the Repositories view's live active/idle
    /// indicator (per specs/repository-management's "active" definition: holds the command lock).
    /// </summary>
    public bool IsHeld(string repositoryRoot) => _activeRunsByRepositoryRoot.ContainsKey(repositoryRoot);

    /// <summary>Releases the lock for <paramref name="repositoryRoot"/>, so a subsequent request for it can be accepted.</summary>
    public void Release(string repositoryRoot)
    {
        if (_activeRunsByRepositoryRoot.TryRemove(repositoryRoot, out var handle))
        {
            _activeRunsByRunId.TryRemove(handle.RunId, out _);
        }
    }

    /// <summary>
    /// Registers an in-flight file transfer under <paramref name="runId"/> so it can be looked
    /// up by <see cref="TryCancel"/>, without acquiring any per-repository lock - per
    /// specs/file-transfer's "Transfers do not contend for the per-repository command lock"
    /// requirement, a transfer is a read of already-produced files, not a command execution, so it
    /// never touches the repository-root-keyed index <see cref="TryAcquire"/>/<see cref="Release"/> use.
    /// </summary>
    public void RegisterTransfer(Guid runId, CancellationTokenSource cancellationTokenSource) =>
        _activeRunsByRunId[runId] = new RunHandle(runId, cancellationTokenSource);

    /// <summary>Removes a file transfer registered via <see cref="RegisterTransfer"/> once it reaches a terminal state.</summary>
    public void ReleaseTransfer(Guid runId) => _activeRunsByRunId.TryRemove(runId, out _);

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

    /// <summary>
    /// Records the root process id spawned for <paramref name="runId"/>, once
    /// <see cref="System.Diagnostics.Process.Start()"/> actually returns one - per
    /// specs/host-resource-monitoring, this is what process-tree discovery roots itself at. A
    /// no-op if the run is no longer tracked (already released). Never called for a transfer or
    /// delete, which spawn no process - <see cref="GetTrackedProcessRoots"/> skips any run with no
    /// recorded id, so those two kinds fall out of process-tree sampling automatically.
    /// </summary>
    public void SetProcessId(Guid runId, int processId)
    {
        if (_activeRunsByRunId.TryGetValue(runId, out var handle))
        {
            handle.ProcessId = processId;
        }
    }

    /// <summary>
    /// Every currently-tracked run that has a recorded root process id - the set the resource
    /// sampler walks a WMI process tree from on each tick, and the set kill-scope verification
    /// (<c>IProcessMonitor.KillAsync</c>) checks a target PID against.
    /// </summary>
    public IReadOnlyList<(Guid RunId, int ProcessId)> GetTrackedProcessRoots() =>
        [.. _activeRunsByRunId.Values
            .Where(h => h.ProcessId is not null)
            .Select(h => (h.RunId, h.ProcessId!.Value))];

    private sealed record RunHandle(Guid RunId, CancellationTokenSource CancellationTokenSource)
    {
        public int? ProcessId { get; set; }
    }
}
