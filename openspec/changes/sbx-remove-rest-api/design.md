## Context

See `proposal.md` for motivation. This is a removal, not a new capability - no new architectural ground to cover. The one design-relevant question is what, if anything, needed genuine reworking (not just deletion) to keep every capability the REST API offered actually reachable afterward, since the instruction driving this change was explicit: nothing existing should break.

## Goals / Non-Goals

**Goals:**
- Every capability the REST API offered remains reachable, through the MCP server (already shipped) or the dashboard (unaffected) - a pure interface removal, not a capability removal.
- No BehaviorTests coverage is lost in the process - REST-only scenarios are deleted only once an equivalent (or, where the REST-era concern genuinely doesn't apply to MCP's shape, a documented non-applicability) exists on the MCP side.
- Leave `Domain`, `Infrastructure`, the persistence schema, and the dashboard completely untouched.

**Non-Goals:**
- Not re-litigating any MCP design decision from `openspec/changes/sbx-mcp-server/` - this change removes REST, it doesn't change how MCP itself works.
- Not archiving `openspec/changes/sbx-dotnet-command-service/` or `openspec/changes/sbx-mcp-server/` - both still describe real, current behavior (dashboard-only capabilities in the former; every MCP tool in the latter), just with their REST-specific requirements removed or reworded in place.

## Decisions

### Direct removal, not a deprecation window
No feature flag, no "REST still works but is marked obsolete" transition period. This project has no shipped release and no production callers - the entire reason a deprecation window normally exists (avoid breaking a caller mid-flight) doesn't apply. Removing outright is simpler and leaves no dead code path to eventually clean up later.

### `transfer_file`'s result gained a `RunId` field
Discovered while porting `RunHistoryPersistence.feature`'s REST-driven steps to MCP tool calls: the test needed a way to identify which persisted `Run` row a given `transfer_file` call produced, and `McpTransferFileResult` didn't expose one (unlike every other MCP tool that touches run history). Added `RunId` to the result - a small, independently-motivated consistency fix, not a REST-removal side effect, but made as part of this change since that's where the gap surfaced.

### Locking/cancellation/concurrency coverage: ported into `McpCommandExecution.feature`, not dropped
`ConcurrencyAndLocking.feature` and `Cancellation.feature` existed purely to exercise REST endpoints, but the underlying behavior they proved (per-repository locking shared across run kinds, cancel-in-flight, cancel-already-finished, cancel-unknown) is exactly as real and exactly as worth testing now that MCP is the only way to reach it. Ported as pure-MCP scenarios directly into `McpCommandExecution.feature` (which already existed and already carried the higher-risk REST-vs-MCP cross-interface scenarios - now replaced by MCP-vs-MCP same-interface and cross-kind scenarios, since there is no second interface left to cross-test against). No coverage gap: `dotnet_run` and `git_run` share one internal start/run-to-completion implementation parameterized only by which executable runs, so proving the lock/cancel mechanism once via `dotnet_run` (or once via each direction of a cross-kind scenario) is exactly as strong a guarantee as the old REST suite's dotnet-vs-git scenarios gave.

### `RunHistoryPersistence.feature`/`HostResourceMonitoring.feature`/`RepositoryManagement.feature`: rewritten in place, not deleted
These three feature files are not about REST at all (restart durability, resource sampling, repository business logic) - they only happened to *drive* their scenario setup via a raw REST call. Rewrote the specific steps that started a run to use the equivalent MCP tool call instead, leaving every other step and every assertion unchanged. `RepositoryManagement.feature` additionally lost one scenario outright ("The REST cancel endpoint does not affect repository-management runs") since it asserted REST-cancel-refuses-a-clone-id behavior that has no MCP analogue - `mcp-repository-access`'s own spec already asserts the *opposite*, intentional MCP behavior (`cancel_run` *does* accept a clone's id), so this wasn't a coverage gap, just a stale assertion about a mechanism that no longer exists.

### Discovered test-infrastructure bug while porting: shared `HttpClient` with an undrained response
Writing the new pure-MCP locking scenarios (a "Given" step leaves a REST-era-style still-open response for a `HangingFixture` run) reproduced the exact class of bug `ConcurrencyAndLockingSteps.cs`'s own remarks already documented and fixed once before: a later request on the same `HttpClient` blocks indefinitely behind a still-open, never-drained one. Fixed the same way - a dedicated `HttpClient` for the still-open probe - and additionally found that a deliberately-never-cancelled `HangingFixture` run's real child process outlives `WebApplicationFactory` disposal (nothing tears it down, since it's detached from any HTTP request lifecycle by the time the run's own tool call already returned), which was making the outer `dotnet test` process take an unpredictably long wall-clock time to actually exit even after all scenarios had already passed - fixed by giving every such scenario a short (3s) caller-supplied timeout instead of relying on the test harness's own teardown to end it.

## Risks / Trade-offs

- **[Risk]** Removing an interface always risks silently dropping test coverage for a real behavior, not just REST-specific mechanics. → Mitigation: every deleted feature file was read in full before deletion (not assumed redundant) and its non-REST-specific scenarios were either already covered by an existing MCP feature file or explicitly ported, per the Decisions above.
- **[Trade-off]** `openspec/changes/sbx-mcp-server/proposal.md`/`design.md` still describe MCP as "additive alongside REST" in their main body text (only a cross-reference note at the top corrects this) - a reader skimming past that note could get a stale impression. → Accepted: those documents are a historical record of *why* MCP was built the way it was (the additive framing genuinely drove several real design decisions, like not touching the REST/SSE code path at all), and rewriting that reasoning after the fact to pretend REST was never a consideration would make the document less useful, not more accurate.

## Migration Plan

No data migration (this changes code and interfaces, not persisted state). Rollout: implement on the existing `feature/sbx-mcp-server` branch (already in flight for the MCP work this change follows directly), verify the full existing test suite plus every ported scenario passes, then this branch's eventual PR carries both the MCP addition and this removal together.
