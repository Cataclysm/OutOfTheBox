## Context

See `proposal.md` for motivation. Relevant existing state this design builds on:

- `RepositoryStatsSampler` (`OutOfTheBox.Infrastructure.Repositories`, a singleton `BackgroundService`) already runs two independent `PeriodicTimer` loops - git status (default 10s) and total size (default 60s) - over every top-level directory under the configured root, plus an event-driven full recompute whenever a run against a specific repository reaches a terminal state. Neither loop touches the network; ahead/behind and "remote gone" are both derived from already-known local refs.
- `IRepositoryManager.FetchAsync(name, ct)` (`RepositoryManager.RunGitActionAsync`, `touchesNetwork: true`) already does everything a background fetch needs: resolves and confines the name, acquires the per-repository lock for the duration (rejecting rather than blocking if busy), runs `git fetch`, records the outcome against `RepositoryCredentialHealth` via `GitAuthFailureClassifier`, and refreshes+publishes stats on success. This is the exact same method the dashboard's per-repository Fetch action already calls.
- `IRepositoryManager` is scoped (it depends on the scoped `IRunRepository`), so a singleton-lifetime `BackgroundService` can't take it as a captive constructor dependency - `GitCredentialStore`/`NuGetFeedCredentialStore` already establish the pattern for this exact constraint: resolve `IServiceScopeFactory`, create a fresh scope per unit of work, resolve the scoped service from it.

## Goals / Non-Goals

**Goals:**
- Every repository under the root gets a real `git fetch` on a regular background cadence, independent of any operator/MCP action, so ahead/behind and "remote gone" stay reasonably fresh even for a repository nobody actively works in.
- Reuse every existing side effect of a fetch (lock, credential-outcome recording, stats refresh) rather than re-deriving any of it for the background path specifically.

**Non-Goals:**
- Not folding this into `RepositoryStatsSampler`'s existing loops - see Decisions.
- Not adding retry/backoff for a repository whose fetch fails - the next tick (5 minutes later, by default) is the retry; a tighter loop for a specifically-failing repository isn't justified by the proposal's problem (staleness), and would make a broken credential noisier (repeated failed network calls) rather than less.
- Not surfacing "last background-fetched at" anywhere in the dashboard/MCP surface - out of scope for a first cut; the effect (fresher ahead/behind) is already directly observable without a separate timestamp.

## Decisions

### A new `RepositoryFetchSampler`, not a third loop inside `RepositoryStatsSampler`
`RepositoryStatsSampler`'s existing two loops are both purely local and cheap (a `git status`/`rev-list` invocation, a recursive directory size walk) - safe to run frequently, and its `ForEachRepositoryAsync` helper spawns process invocations directly via `IProcessRunner`, never going through `IRepositoryManager`'s per-repository lock at all (it doesn't need to; nothing it does conflicts with an in-flight run). A network fetch is a different kind of operation: it can genuinely take real seconds against a slow remote or a large repository, must hold the per-repository lock for its duration (so it never races an operator/MCP-triggered command against the same repository), and needs `IRepositoryManager.FetchAsync`'s full behavior (credential-outcome recording included) rather than a bare `git fetch` invocation. Bolting that onto `RepositoryStatsSampler` would mean giving one of its loops a completely different dependency shape (a scoped `IServiceScopeFactory`-resolved `IRepositoryManager` instead of the singleton `IRepositoryStatsProvider`) and a completely different cadence rationale (network-bound, not local-compute-bound) - a new, small, single-purpose sampler class is more consistent with this project's existing precedent of one focused `BackgroundService` per concern (`RepositoryStatsSampler` for stats, `HostResourceSamplerService` for host/process resources, each documented as "distinct from, and runs at different cadences than" the other).

### Goes through `IRepositoryManager.FetchAsync`, not a raw `git fetch` `IProcessRunner` call
Considered spawning `git fetch` directly (mirroring `RepositoryStatsSampler.ForEachRepositoryAsync`'s own approach) and rejected: that would skip the per-repository lock entirely (risking a background fetch racing an operator's own in-flight pull/push against the same repository - unlike the stats sampler's read-only local commands, `git fetch` does mutate local state, namely the remote-tracking refs) and would need its own separate credential-outcome recording call, duplicating `RecordCredentialOutcomeAsync`'s logic for no benefit. Calling `FetchAsync` directly means this sampler's own code is a thin sweep loop - list repositories, call `FetchAsync` for each git one, log a real failure - with zero git-specific logic of its own.

### A busy repository is skipped for that tick, not queued or retried early
`FetchAsync`'s `Rejected(Busy, ...)` result (the per-repository lock already held by another run) is treated as a routine, unlogged outcome - the same repository gets tried again at the *next* regular tick, 5 minutes later by default, rather than this sampler polling more tightly around a busy repository or queuing a deferred retry. Simpler, and consistent with the proposal's own framing: this is a "stay reasonably fresh" background hygiene task, not a guarantee that every repository gets fetched on every single tick.

## Risks / Trade-offs

- **[Trade-off]** A repository with many, or slow, remotes could make one sweep take noticeably longer than the configured interval, since fetches run sequentially (mirroring `RepositoryStatsSampler.ForEachRepositoryAsync`'s own sequential-not-parallel choice, to avoid spiking CPU/network with many concurrent `git` processes). Accepted: `PeriodicTimer`'s own semantics mean an overrunning tick simply delays the next one rather than queuing up duplicate work, the same behavior `RepositoryStatsSampler`'s two loops already have.
- **[Trade-off]** A private repository with no working credential gets a real network round trip (and a failed `git fetch`) every 5 minutes indefinitely, until an operator fixes it. Accepted, and actually useful: it's what makes this sweep double as a self-healing credential-health check (see proposal.md) rather than only ever reflecting whatever an operator last happened to trigger.

## Migration Plan

Purely additive - one new `BackgroundService`, one new `ServiceOptions` field (default 300s), no schema/config-breaking changes, no impact on `RepositoryStatsSampler`'s existing two loops or any dashboard/MCP-facing contract beyond what `FetchAsync` already produces for an operator-triggered fetch. No feature flag - a background sampler has no separate reachability surface to gate. Ship in the next installer build.
