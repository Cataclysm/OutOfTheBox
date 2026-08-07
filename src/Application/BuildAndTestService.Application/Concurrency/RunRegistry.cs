using System.Collections.Concurrent;

namespace BuildAndTestService.Application.Concurrency;

/// <summary>
/// Tracks which repos currently have an in-flight run, enforcing at most one <c>dotnet</c> command
/// per repo at a time while allowing distinct repos to run fully in parallel (per
/// specs/dotnet-command-execution's "Commands against different repos run in parallel" and
/// "One in-flight command per repo" requirements). Registered as a singleton - this is
/// process-wide, in-memory, and lost on restart by design (see design.md's Risks).
/// </summary>
public sealed class RunRegistry
{
    private readonly ConcurrentDictionary<string, Guid> _activeRunsByRepoRoot = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Attempts to acquire the lock for <paramref name="repoRoot"/> (the exact resolved working
    /// directory of the request, not a higher-level grouping) on behalf of <paramref name="runId"/>.
    /// Atomic: under concurrent callers targeting the same repo, exactly one succeeds.
    /// </summary>
    /// <param name="repoRoot">The resolved working directory to lock.</param>
    /// <param name="runId">The run id requesting the lock.</param>
    /// <param name="conflictingRunId">
    /// When this method returns <see langword="false"/>, the run id already holding the lock;
    /// otherwise <see langword="default"/>.
    /// </param>
    public bool TryAcquire(string repoRoot, Guid runId, out Guid conflictingRunId)
    {
        if (_activeRunsByRepoRoot.TryAdd(repoRoot, runId))
        {
            conflictingRunId = default;
            return true;
        }

        conflictingRunId = _activeRunsByRepoRoot[repoRoot];
        return false;
    }

    /// <summary>Releases the lock for <paramref name="repoRoot"/>, so a subsequent request for it can be accepted.</summary>
    public void Release(string repoRoot)
    {
        _activeRunsByRepoRoot.TryRemove(repoRoot, out _);
    }
}
