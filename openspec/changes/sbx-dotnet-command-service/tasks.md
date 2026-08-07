## 1. Project Setup

- [x] 1.1 Create the `.slnx` solution at the repo root
- [x] 1.2 Create solution folders: `Domain`, `Application`, `Infrastructure`, `Presentation`, `Host`, `Tests`
- [x] 1.3 Scaffold `src/Domain/BuildAndTestService.Domain` (plain `net10.0` class library, no NuGet dependencies beyond the BCL), added to the `Domain` solution folder
- [x] 1.4 Scaffold `src/Application/BuildAndTestService.Application` (plain `net10.0` class library, references `Domain` only), added to the `Application` solution folder
- [x] 1.5 Scaffold `src/Infrastructure/BuildAndTestService.Infrastructure` (`net10.0-windows` class library, references `Application` + `Domain` only), added to the `Infrastructure` solution folder
- [x] 1.6 Scaffold `src/Presentation/BuildAndTestService.Presentation` (plain `net10.0` Razor Class Library — `Sdk="Microsoft.NET.Sdk.Razor"` — carrying `.razor` components, `wwwroot`, minimal API endpoint-mapping extension methods, and auth middleware; references `Application` + `Domain` only, **no reference to `Infrastructure`**), added to the `Presentation` solution folder
- [x] 1.7 Scaffold `src/Host/BuildAndTestService.Host` (`net10.0-windows` ASP.NET Core executable — the only project referencing all four others), added to the `Host` solution folder
- [x] 1.8 In `Host`'s `Program.cs`: build the `WebApplication` and bind `ServiceOptions`; a marked placeholder notes where `Presentation`'s endpoint-mapping and `Infrastructure`'s DI registrations land as those pieces are built in later sections; Kestrel/HTTPS hardening deferred to §13 Transport & Network
- [x] 1.9 Add `Microsoft.Extensions.Hosting.WindowsServices` to `Host` and configure `UseWindowsService()`
- [x] 1.10 Add configuration schema (appsettings.json + env var overrides via the default ASP.NET Core config provider chain) for: root directory, bearer token, listen port, default execution timeout (10 minutes), maximum execution timeout, output size cap, SQLite file path — bound to a `ServiceOptions` type
- [x] 1.11 Enable `<Nullable>enable</Nullable>` and `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (with `CS1591` as an error) on every project via a shared `Directory.Build.props`

## 2. Test Suite Foundation

- [x] 2.1 Scaffold `tests/UnitTests/BuildAndTestService.UnitTests` (xUnit, `Microsoft.NET.Test.Sdk`), references `Domain`, `Application`, `Infrastructure` — no real process spawning, no real network; fakes/mocks for `Process`, `IRunEventBus`, `IResourceEventBus`, a real SQLite `:memory:` connection for repository-logic tests, and the internal `Domain`/`Application`/`Infrastructure` folder split all land as each layer gains real test content in later sections. **Correction from design.md**: since it references `Infrastructure` (`net10.0-windows`), a plain `net10.0` project can't reference it — `UnitTests` (and `BehaviorTests`/`ArchitectureTests`, both of which reference `Host`/`Infrastructure`) target `net10.0-windows` too, not plain `net10.0`
- [x] 2.2 Scaffold `tests/BehaviorTests/BuildAndTestService.BehaviorTests` (Reqnroll.xUnit + `Microsoft.AspNetCore.Mvc.Testing`), references `Host`, for Gherkin `.feature`-file scenarios exercised against the real ASP.NET Core pipeline via `WebApplicationFactory<Program>`/`TestServer` — required exposing Host's implicit `Program` class via a trailing `public partial class Program;` so another assembly can reference it
- [x] 2.3 Scaffold `tests/ArchitectureTests/BuildAndTestService.ArchitectureTests` (xUnit + `NetArchTest.Rules`), referencing all five projects (only for reflection over their assemblies, not for calling into them)
- [x] 2.4 Write the NetArchTest rules in `LayeringTests.cs`: `Domain` has no dependency on `Application`/`Infrastructure`/`Presentation`/`Host` or on ASP.NET Core/EF Core/`System.Management`/`PerformanceCounter` namespaces; `Application` has no dependency on `Infrastructure`/`Presentation`/`Host`; `Infrastructure` has no dependency on `Presentation`/`Host`; `Presentation` has no dependency on `Infrastructure`/`Host` — no exception for any of these; `Host` is excluded from the rule set entirely (it's the composition root, expected to reference everything)
- [x] 2.5 Added `UnitTests`, `BehaviorTests`, `ArchitectureTests` under the `Tests` solution folder in the `.slnx`. **Deviation**: the `Fixtures` projects are deliberately *not* added to the `.slnx` — they live as plain standalone `.csproj`s under `tests/Fixtures/`, never solution-registered, because a failing/hanging test inside them would otherwise break `dotnet test` on the main solution (they're targets the service spawns `dotnet` against, not part of this repo's own test run)
- [x] 2.6 Added Reqnroll.xUnit and confirmed a trivial scenario (`Sanity.feature`) runs end-to-end via plain `dotnet test` — passed, 1/1
- [x] 2.7 Added `tests/Fixtures/`: `PassingFixture` (one passing test), `FailingFixture` (one deliberately-wrong assertion), `HangingFixture` (one test that awaits `Task.Delay(Timeout.Infinite)` and never returns) — verified each behaves as intended (pass / fail with a clear assertion message / genuinely hangs until killed)
- [ ] 2.8 Write one Gherkin feature file per capability (`dotnet-command-execution.feature`, `service-authentication.feature`, `run-history.feature`, `service-dashboard.feature`, `host-resource-monitoring.feature`), with each scenario's Given/When/Then derived directly from that capability's spec.md `#### Scenario:` blocks, so spec and executable test stay in lockstep — **deferred**: per design.md's "land alongside each piece" plan, each capability's feature file is written in its own implementation section (§3, §5/§6/§8, §9, §10, §11), not upfront here, since a feature file with no matching step definitions would sit as an undefined/pending scenario until that capability actually exists
- [x] 2.9 Test convention documented in `design.md`'s "Test project layout" decision (unit tests cover isolated Domain/Application/targeted-Infrastructure logic; BDD feature files cover spec.md scenarios; NetArchTest covers layering; real-deployment checks stay manual in §16)
- [x] 2.10 Confirmed `dotnet build BuildAndTestService.slnx` and `dotnet test BuildAndTestService.slnx` both succeed clean (0 warnings/errors; ArchitectureTests 5/5, BehaviorTests 1/1, UnitTests 0 tests — expected, no test content yet)
- [x] 2.11 Sanity-checked the NetArchTest rules: temporarily referenced a `Presentation` type from `Infrastructure`, confirmed `Infrastructure_has_no_dependency_on_presentation` failed and named the exact offending type, then fully reverted (file removal + `dotnet remove reference`) and reconfirmed a clean 5/5 pass

## 3. Authentication

- [x] 3.1 Implemented `BearerAuthenticationFilter` (`IEndpointFilter`, in `Presentation`) requiring an `Authorization: Bearer <token>` header — built as a reusable filter, not yet attached to the command-execution/cancellation endpoints since those are built in §5/§8 (`.AddEndpointFilter<BearerAuthenticationFilter>()` happens when those endpoints are mapped)
- [x] 3.2 Filter returns `Results.Unauthorized()` and short-circuits (never calls `next`) before any handler runs, for both a missing and a wrong credential
- [x] 3.3 Comparison implemented in `Domain.Authentication.CredentialComparer` via `CryptographicOperations.FixedTimeEquals` — kept in `Domain` since it's pure logic with zero framework dependency, callable from `Presentation`'s filter
- [x] 3.4 Expected credential read from `ServiceOptions.BearerToken`, bound from configuration in `Host`'s `Program.cs` — **correction**: `ServiceOptions` was relocated from `Host` to `Application.Configuration`, since `Presentation` (which needs it for the filter) cannot depend on `Host` per the architecture rule; `Host` still owns binding the actual `IConfiguration` values into it
- [x] 3.5 Unit tests in `CredentialComparerTests.cs`: identical/different/different-length/case-sensitivity/missing-provided/empty-expected — 7 cases, all passing
- [x] 3.6 BDD in `ServiceAuthentication.feature` (4 scenarios: missing/valid/wrong credential + rotation), driving the real filter through `EndpointFilterInvocationContext.Create` (the documented pattern for unit-testing `IEndpointFilter` without a live host) rather than a full `WebApplicationFactory` run, since no endpoint exists yet to attach it to — the literal "does not invoke `dotnet.exe`" claim from spec.md gets additional coverage once §5 wires the real endpoint

## 4. Path Confinement

- [x] 4.1 Implemented in `Infrastructure.Execution.WorkingDirectoryResolver`: joins the caller-supplied relative path to `ServiceOptions.RootDirectory` via `Path.Combine` + `Path.GetFullPath` (an absolute caller-supplied path correctly discards the root per `Path.Combine`'s own semantics, so it's rejected by containment rather than needing a special case)
- [x] 4.2 `WorkingDirectoryResolver.ResolveSymlinkTarget` calls `Directory.ResolveLinkTarget(path, returnFinalTarget: true)` before the containment check; a real symlink-escape test confirms it (skips gracefully if the sandbox lacks the privilege to create symlinks, per Windows' Developer Mode/elevation requirement)
- [x] 4.3 Containment decision implemented in `Domain.PathConfinement.PathConfinementPolicy.IsContained` (full-path comparison with a trailing separator, not naive `StartsWith`) — pure, zero-IO, called by the Infrastructure resolver above
- [x] 4.4 Unit tests: `PathConfinementPolicyTests` (7 cases: root itself, subdirectory, sibling-prefix rejection, unrelated path, parent-of-root, case-insensitivity, trailing-separator handling) + `WorkingDirectoryResolverTests` (5 cases against a real temp directory tree: valid subdirectory, `../..` traversal, absolute-path escape, sibling-prefix escape, symlink escape)

## 5. Command Execution

- [x] 5.1 Contract implemented: `StartRunRequest` (Presentation) — `Arguments`/`WorkingDirectory`/`TimeoutSeconds` in; `X-Run-Id` response header; `SseWriter` (Presentation) emits `stdout`/`stderr` data events, a terminal `done` event (`{exitCode, truncated}`), and a terminal `error` event (`{reason}`, values `validation`/`timeout`/`cancelled`)
- [x] 5.2 `DotnetProcessRunner.BuildStartInfo` (Infrastructure): `UseShellExecute = false`, arguments added individually to `ArgumentList` — never string-concatenated
- [x] 5.3 `OutputDataReceived`/`ErrorDataReceived` write into an unbounded `Channel`; a single consumer task drains it into `IProcessOutputSink` in order, avoiding concurrent writes to the HTTP response from the two callback threads
- [x] 5.4 `RunEndpoints.HandleStartRunAsync` sets `Content-Type: text/event-stream`, disables response buffering via `IHttpResponseBodyFeature`, and `SseWriter` flushes after every event
- [x] 5.5 `SseProcessOutputSink` tracks cumulative UTF-8 byte count against `ServiceOptions.OutputCapBytes`; once exceeded, further lines are dropped (`Truncated = true`) but the process keeps running
- [x] 5.6 `Domain.Runs.ExecutionTimeoutPolicy.Resolve` computes the effective timeout (caller value or default, clamped to configured maximum); the endpoint links a timeout `CancellationTokenSource` with `HttpContext.RequestAborted`, and `DotnetProcessRunner` kills the process tree (`entireProcessTree: true`) on cancellation
- [x] 5.7 Validation failures (empty/missing arguments, missing working directory, path-confinement rejection) write `error`/`validation` before any process is started — the "repo already locked" case is added when Section 6's registry wraps this endpoint
- [x] 5.8 BDD `DotnetCommandExecution.feature`: successful `dotnet test` round trip against `PassingFixture` — run id present, output events precede `done`, exit code 0
- [x] 5.9 BDD: `dotnet test` against `FailingFixture` returns a non-zero exit code via `done`, not a transport-level error
- [x] 5.10 Unit tests on `DotnetProcessRunner.BuildStartInfo` (no live process needed): `UseShellExecute` is false, and an argument containing shell metacharacters (`; rm -rf / & echo INJECTED > pwned.txt`) round-trips as exactly one literal `ArgumentList` entry
- [x] 5.11 BDD: a non-streaming client (`HttpCompletionOption.ResponseContentRead`, fully buffered) parses the identical set of events as the streaming client
- [x] 5.12 Unit tests on `ExecutionTimeoutPolicy`: caller value shorter than default honored; caller value longer than maximum clamped; omitted caller value uses default; a misconfigured default above maximum is also clamped
- [x] 5.13 BDD: both "no caller timeout, short configured default" and "short caller-supplied timeout" scenarios against `HangingFixture` end in `error`/`timeout` — confirmed no orphaned `testhost.exe`/`dotnet.exe` processes survive the kill

All 10 BehaviorTests scenarios pass in ~14s (real `dotnet.exe` spawns); 27/27 UnitTests; 5/5 ArchitectureTests.

## 6. Concurrency & Locking

- [x] 6.1 Implemented `Application.Concurrency.RunRegistry`: `ConcurrentDictionary<string, Guid>` keyed by the resolved repo root (case-insensitive), mapping to the holding run's id — **simplified from the original `RunInfo` sketch**: the `Process` handle and `CancellationTokenSource` stay local to `RunEndpoints.HandleStartRunAsync`/`DotnetProcessRunner` (they're not needed anywhere else yet); the registry's only job is the lock. Section 8 (Cancellation) will extend it with whatever run-id → cancellation-handle lookup cancelling actually needs
- [x] 6.2 `RunEndpoints` calls `RunRegistry.TryAcquire` (atomic `ConcurrentDictionary.TryAdd`) right after path resolution and before any process is started; on failure, writes an `error` SSE event with reason `validation` and a `runId` field naming the conflicting run — extended `SseWriter.WriteErrorAsync` to carry that optional field
- [x] 6.3 `RunRegistry.Release` called in a `finally` block wrapping process execution, so the lock is freed on every terminal path (completed, timed out) without duplicating release logic per branch
- [x] 6.4 Unit test: 50 concurrent callers racing `TryAcquire` for the same repo — exactly one succeeds (`Barrier`-synchronized to maximize actual contention, not just sequential calls)
- [x] 6.5 BDD `ConcurrencyAndLocking.feature`: `PassingFixture` and `FailingFixture` runs started concurrently (not sequentially awaited) both complete independently
- [x] 6.6 BDD: a second request against an in-flight `HangingFixture` run is rejected, `error`/`validation` carrying the in-flight run's exact id (captured via the first request's `X-Run-Id` header, available immediately since `RunEndpoints` now calls `response.StartAsync()` right after setting headers rather than waiting for the first event write)
- [x] 6.7 BDD: once the in-flight run reaches a terminal state (its own short timeout), a same-repo follow-up request is accepted (no busy-repo rejection) — note it still may itself time out against `HangingFixture`, which is a different, legitimate outcome the test distinguishes from a conflict rejection

All 13 BehaviorTests scenarios pass in ~14s; 32/32 UnitTests; 5/5 ArchitectureTests.

## 7. Documentation, Copyright, and Project Conventions

- [x] 7.1 Created `README.md`: what this project is, links to `BUILD.md`/`INSTALL.md`/`CHANGELOG.md`/`CLAUDE.md`, architecture summary, pointer to `openspec/changes/sbx-dotnet-command-service/` for the full design rationale
- [x] 7.2 Created `BUILD.md`: prerequisites, `dotnet build`/`dotnet test` on `BuildAndTestService.slnx`, the fast (`UnitTests`/`ArchitectureTests`) vs. slow (`BehaviorTests`) split and why, plus a note on the `Fixtures`-not-in-`.slnx` deviation and the doc-comment build gate
- [x] 7.3 Created `INSTALL.md`: today's dev-run instructions (`dotnet run --project src/Host/BuildAndTestService.Host`, required config keys), with a clearly-marked "Planned" section for §14's `install.ps1`/`upgrade.ps1` flow, to be rewritten with concrete instructions once that section lands
- [x] 7.4 Created `CHANGELOG.md` (Keep a Changelog format) with an `Unreleased` section covering Sections 1–6 (Added) and a pointer to `tasks.md` for what's still in progress
- [x] 7.5 Added the copyright header (`// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.`, from `git config user.name`/`user.email`) to all 33 existing `.cs` files under `src/` and `tests/{UnitTests,BehaviorTests,ArchitectureTests}` via a script (checked each file's first line first, so it's safe to re-run) — excluded `tests/Fixtures/` and the Reqnroll-generated `*.feature.cs` files (regenerated every build, already gitignored, a header would just vanish)
- [x] 7.6 Convention adopted for all subsequent sections: every new `.cs` file starts with the same header
- [x] 7.7 Initialized `CLAUDE.md` via the `init` skill: commands, the Clean Architecture placement quick-reference with the "why `Presentation` can't depend on `Infrastructure`, not even for DI" rationale, and the conventions list (copyright header, doc-comment gate, `Fixtures` exclusion, commit-per-coherent-step, feature-files-land-with-their-capability)
- [x] 7.8 Confirmed `dotnet build` and the full `dotnet test` (all three projects) still succeed after header insertion: 0 warnings/errors, 32/32 UnitTests, 5/5 ArchitectureTests, 13/13 BehaviorTests — also caught and fixed one now-stale in-code comment (`Program.cs` said "Section 12 (Transport & Network)"; renumbered to §13 along with this section's insertion)

## 8. Cancellation API

- [x] 8.1 Already satisfied by §5/§6: `RunEndpoints` assigns `runId` and calls `response.StartAsync()` right after setting the `X-Run-Id` header, before any validation or the SSE body begins
- [x] 8.2 Added authenticated `POST /run/{runId:guid}/cancel`, mapped alongside `POST /run` in `MapCommandExecutionEndpoints`
- [x] 8.3 `RunRegistry` extended with a run-id-keyed index (`ConcurrentDictionary<Guid, RunHandle>`, alongside the existing repo-root index) storing a dedicated `cancelRequestCts` per run; `TryCancel(runId)` calls `.Cancel()` on it, which (linked into the same token passed to `IProcessRunner.RunAsync`) triggers `Process.Kill(entireProcessTree: true)` via the same cancellation-registration mechanism the timeout path already used
- [x] 8.4 The `cancelRequestCts` is a **separate** linked-token source from `timeoutCts` (both linked into one `linkedCts` alongside `RequestAborted`), so `RunEndpoints` can tell which one fired and emit `error`/`cancelled` vs `error`/`timeout` correctly instead of collapsing both into one reason; the registry entry is removed in the same `finally` block as before
- [x] 8.5 Cancel for an unknown/already-terminal run id returns `404 Not Found`; `RunRegistry.TryCancel` also catches `ObjectDisposedException` (a benign race if the run's `CancellationTokenSource` gets disposed between lookup and `.Cancel()`) and treats it identically to not-found — no side effects either way
- [x] 8.6 BDD `Cancellation.feature`: cancelling an in-flight `HangingFixture` run returns 202, its SSE stream ends with `error`/`cancelled`, and a subsequent request against the same repo is accepted (not rejected as busy)
- [x] 8.7 BDD: cancelling an already-completed run (`PassingFixture`, awaited to completion first) and cancelling an unknown run id both return 404; cancelling the same run twice returns 404 the second time **once the first cancellation has actually completed** (drained via the "stream ends with reason" step) — an earlier version of this scenario fired the second cancel immediately and incorrectly expected 404 while the run was still legitimately in-flight (cancellation requested but not yet terminated); fixed by sequencing the test correctly rather than changing the implementation, since spec.md only requires 404 for unknown-or-already-terminal runs

All 17 BehaviorTests scenarios pass in ~14s; 36/36 UnitTests; 5/5 ArchitectureTests (58/58 total). No orphaned `testhost.exe` after cancel-triggered kills.

## 9. Persistence (Run History)

- [ ] 9.1 Add EF Core + SQLite provider packages; define `Run` entity (`Id`, `RepoPath`, `Arguments`, `StartedAt`, `CompletedAt`, `Outcome`, `ExitCode`, `Stdout`, `Stderr`, `Truncated`) and initial migration
- [ ] 9.2 Apply pending migrations against the configured SQLite file path at startup
- [ ] 9.3 Enable WAL journal mode on the SQLite connection
- [ ] 9.4 Insert a `Run` row with `Outcome = Running` at the same point the in-memory registry entry is created
- [ ] 9.5 Update the `Run` row's `CompletedAt`/`Outcome`/`ExitCode`/`Stdout`/`Stderr`/`Truncated` at the same point the registry entry is removed (single code path for "run is over")
- [ ] 9.6 On startup, reconcile any row still `Outcome = Running` from a previous process to `Outcome = Interrupted`
- [ ] 9.7 Add a query for listing runs most-recent-first (summary fields only) and a query for one run's full record by id
- [ ] 9.8 Unit test (SQLite `:memory:`): a run's row exists with `Outcome = Running` while in flight, then is updated to its terminal outcome with full output on completion
- [ ] 9.9 Unit test: persisted stdout/stderr and truncation flag match what was streamed for a truncated run
- [ ] 9.10 BDD: a run's history record is retrievable after a service restart
- [ ] 9.11 Unit test: a row left `Running` by a simulated crash is reconciled to `Interrupted` on next startup
- [ ] 9.12 Define `RunResourceSample` entity (`RunId` FK, `Timestamp`, `CpuPercent`, `RamBytes`) with an index on `(RunId, Timestamp)`, and its migration
- [ ] 9.13 Add a query returning a run's complete resource-sample series ordered by `Timestamp`, for both in-flight and completed runs
- [ ] 9.14 BDD: a completed run's full resource-sample series (start to terminal state) is retrievable regardless of run duration

## 10. Dashboard (Blazor Server)

- [ ] 10.1 Add Blazor Server to the existing minimal API project (`AddServerSideBlazor`, root component host page)
- [ ] 10.2 Implement `IRunEventBus` singleton (run-started / output-line / run-terminal events) published to from the same execution code paths as the registry and the SQLite writes
- [ ] 10.3 Build login page: operator enters the shared token, validated via the same `CryptographicOperations.FixedTimeEquals` check, issues an auth cookie on success
- [ ] 10.4 Require the auth cookie on all dashboard routes/circuits; unauthenticated access shows no run data
- [ ] 10.5 Add a single dark CSS theme; no light-mode stylesheet, no `prefers-color-scheme` branching, no toggle
- [ ] 10.6 Build top-level navigation with two views: **Status** (default) and **History**
- [ ] 10.7 Build Status view's run list: in-flight runs (repo, args, run id, start time, elapsed time) from the in-memory registry, subscribed to `IRunEventBus` for live updates via `StateHasChanged`
- [ ] 10.8 Build "idle" empty-state for the Status view when no runs are in flight
- [ ] 10.9 Build History view's list: queries `Runs` table most-recent-first with summary fields
- [ ] 10.10 Build run-detail view: full command, repo, timestamps, outcome, complete stdout/stderr, truncation indicator, by run id
- [ ] 10.11 BDD: a new run appears in the Status view live, without reload, when started by another client (`service-dashboard.feature`)
- [ ] 10.12 BDD: a run's completion is reflected live (status updates / moves to history) without reload
- [ ] 10.13 BDD: History view and run-detail view render correctly for completed, timed-out, cancelled, and interrupted runs
- [ ] 10.14 Add a `/version` endpoint (or dashboard footer) exposing the running build's assembly version, for upgrade verification

## 11. Host Resource Monitoring

- [ ] 11.1 Implement host CPU sampling via `PerformanceCounter` (`\Processor(_Total)\% Processor Time` and per-core `\Processor(N)\% Processor Time`), discarding each counter's first (always-zero) reading
- [ ] 11.2 Implement host RAM sampling via `GlobalMemoryStatusEx` P/Invoke (total/available physical memory)
- [ ] 11.3 Implement service-process RAM via `Process.GetCurrentProcess().WorkingSet64`
- [ ] 11.4 Implement process-tree discovery: WMI `Win32_Process` query by `ParentProcessId`, recursively rooted at each tracked run's `Process.Id` from the run registry
- [ ] 11.5 Implement per-process CPU% via delta-sampling `Process.TotalProcessorTime` between ticks, and per-process RAM via `WorkingSet64`
- [ ] 11.6 Implement a background `PeriodicTimer`-based sampler (configurable interval, default a few seconds) that samples host + per-run-tree data each tick and publishes a snapshot to a new `IResourceEventBus`
- [ ] 11.7 Implement `IProcessMonitor.KillAsync(pid)`: re-verify `pid` is currently part of a tracked run's process tree (including re-checking `Process.StartTime` to guard against PID reuse) immediately before killing; reject if not found; call `Process.Kill(entireProcessTree: true)` on the verified target
- [ ] 11.8 Wire the Status view's per-run process sublist to `IResourceEventBus`, grouped under its owning run (not one global flat table), with a kill button per process calling `IProcessMonitor.KillAsync` directly (no new HTTP endpoint)
- [ ] 11.9 Add host CPU (total + per-core) and RAM (total + service) tiles to the Status view header
- [ ] 11.10 Unit test: total and per-core CPU figures and total/service RAM figures update on the configured interval (using a fake clock/timer)
- [ ] 11.11 BDD: a run's spawned children (e.g. a `testhost.exe` under a `dotnet test` run) appear in that run's process sublist with plausible CPU/RAM values (`host-resource-monitoring.feature`)
- [ ] 11.12 BDD: killing a listed process terminates it and its descendants, and it disappears from the list on the next refresh
- [ ] 11.13 Unit test: `IProcessMonitor.KillAsync` rejects a PID outside any tracked run's process tree
- [ ] 11.14 BDD: process list is empty when no runs are in flight, with no stale entries from a previous run
- [ ] 11.15 Unit test: a run's aggregate CPU%/RAM figure equals the sum of that tick's per-process values across its tree
- [ ] 11.16 Unit test (SQLite `:memory:`): one `RunResourceSample` row is written per in-flight run per tick, stopping once the run reaches a terminal state
- [ ] 11.17 Maintain an in-memory 10-minute circular buffer per live series (host total/per-core, each in-flight run) fed by the same tick, independent of the SQLite write
- [ ] 11.18 Unit test: the circular buffer evicts points older than 10 minutes while the persisted series (from 11.16) keeps every point

## 12. Performance Graphs

- [ ] 12.1 Vendor `chart.js` under `Presentation`'s `wwwroot` (no CDN reference)
- [ ] 12.2 Build a minimal `IJSRuntime` interop wrapper: create a chart instance, push incremental points (`chart.data.datasets[...].data.push(...); chart.update('none')`), destroy on component dispose
- [ ] 12.3 Build host CPU/RAM graph components in the Status header, always live, backed by the host in-memory circular buffers
- [ ] 12.4 Build a per-run CPU/RAM graph component, lazily mounted only when that run's card is expanded, backed by that run's in-memory circular buffer
- [ ] 12.5 Build a run-detail graph component for the History view, backed by the full `RunResourceSample` series for that run (not windowed)
- [ ] 12.6 BDD: host graphs render and extend live as new samples arrive, showing at least the trailing 10 minutes
- [ ] 12.7 BDD: an in-flight run's graph, once expanded, shows that run's own recent usage and continues updating live
- [ ] 12.8 BDD: a completed run's history detail graph spans its entire recorded duration, including runs longer than 10 minutes
- [ ] 12.9 Unit test: a run's live chart component is not instantiated (no interop calls, no per-run subscription) while its card is collapsed

## 13. Transport & Network

- [ ] 13.1 Configure Kestrel to require HTTPS on the configured port
- [ ] 13.2 Document/generate the certificate used (self-signed acceptable for v1) and how the sbx-side client pins/trusts it
- [ ] 13.3 Document Windows Firewall inbound rule scoping the command-API port to the sbx sandbox's IP, and the dashboard port/path to the operator's network
- [ ] 13.4 Document required sbx-side client behavior for consuming SSE (e.g. `curl -N`, or HttpClient with `HttpCompletionOption.ResponseHeadersRead`) so responses aren't fully buffered before use

## 14. Packaging & Install/Upgrade

- [ ] 14.1 Configure `Host` for self-contained, single-file, win-x64 publish (`--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`, trimming disabled); confirm publish targets only `src/Host/BuildAndTestService.Host`, not `Domain`/`Application`/`Infrastructure`/`Presentation` or the test projects individually (those are pulled in transitively as compiled dependencies and composed static web assets, not published as separate outputs)
- [ ] 14.2 Verify the published exe runs standalone on a clean Windows machine with no .NET runtime installed
- [ ] 14.3 Define the data directory layout (e.g. `%ProgramData%\BuildAndTestService\`) holding config and the SQLite file, separate from the install directory
- [ ] 14.4 Write `install.ps1`: create a new dedicated local service account (least-privilege, not local admin); grant it log-on-as-a-service, read/write on the data directory, read/write on the configured repo root, and "Performance Monitor Users" membership; create install + data directories; copy the exe and publish-output files (including `wwwroot`); write initial config (root directory, bearer token, port, default/maximum timeout, output cap, SQLite path); create the Windows Service running as that account; configure SCM crash-recovery via `sc.exe failure`; open the required firewall rule(s); start the service; verify `chart.js` exists at its expected path
- [ ] 14.5 Write `upgrade.ps1`: stop the service and wait/verify it reaches `Stopped` (abort with a clear error on timeout); copy the new exe and publish-output files over the install directory only; start the service; poll `/version` to confirm the new build is running
- [ ] 14.6 Document the dedicated service account `install.ps1` creates (not local admin, not a pre-existing/shared account) and exactly what rights it's granted and why
- [ ] 14.7 Document that the run registry is in-memory only: a restart drops all lock/run-id state (cancel calls for pre-restart run ids will 404; repos locked before restart become immediately available; SQLite rows reconcile to `Interrupted`)
- [ ] 14.8 Document that the data directory (config + SQLite file) is untouched by `upgrade.ps1` and by reinstall, so history and configuration survive both
- [ ] 14.9 Document that downgrade is unsupported (migrations are forward-only) and recommend operators copy the SQLite file before upgrading if they want a manual rollback point
- [ ] 14.10 Manual test: `install.ps1` on a clean host results in a running, reachable, authenticated service with an empty history, working resource monitoring (confirming the Performance Monitor Users grant took effect), and working graphs (confirming `wwwroot`/`chart.js` was copied)
- [ ] 14.11 Manual test: `upgrade.ps1` from one published build to a newer one preserves configuration and existing history (including resource samples), and `/version` reflects the new build afterward
- [ ] 14.12 Manual test: killing the service process outright (simulated crash) results in SCM restarting it per the configured recovery actions

## 15. Claude Code Skill

- [ ] 15.1 Create `skills/dotnet-command-service/SKILL.md` with frontmatter (name, description) identifying it as the client guide for calling this service
- [ ] 15.2 Document authentication: bearer header name/format, where the token value comes from (the sbx-side operator's own configuration, not hardcoded)
- [ ] 15.3 Document starting a run: `POST /run` request shape (argument list, working directory, optional timeout), reading the `X-Run-Id` response header
- [ ] 15.4 Document consuming the SSE stream from a Bash-based agent: the `curl -N` / background-process-and-poll pattern, the `stdout`/`stderr`/`done`/`error` event types, and the `error` reasons (`validation`, `timeout`, `cancelled`)
- [ ] 15.5 Document cancelling a run: `POST /run/{runId}/cancel`, and what a 404 (unknown/already-finished run) means
- [ ] 15.6 Document error responses relevant to the caller: 401 (missing/invalid credential), 409 (repo busy, with the blocking run's id)
- [ ] 15.7 Explicitly scope the skill to API consumption only — no dashboard, resource-monitoring, or install/upgrade content, since those aren't for the sbx caller
- [ ] 15.8 Cross-reference which spec files (`specs/dotnet-command-execution`, `specs/service-authentication`) the skill restates, as a marker for keeping it in sync when the API changes
- [ ] 15.9 Manual test: have a real Claude Code instance follow only the skill's instructions (no other context) to authenticate, start a run against the passing-test fixture, observe streamed output, and cancel a separate hanging run — confirm it succeeds without needing to read the specs directly

## 16. End-to-End Verification (manual, real deployment)

- [ ] 16.1 From a real remote caller, send an authenticated `dotnet --version` request and confirm round trip
- [ ] 16.2 Run a real `dotnet build` and `dotnet test` against a sample repo through the service and confirm results match running the same commands locally
- [ ] 16.3 Confirm unauthenticated and path-escaping requests are rejected end-to-end
- [ ] 16.4 Confirm parallel commands against two different repos both complete, and confirm a second command against a busy repo is rejected with the busy run's id
- [ ] 16.5 Confirm cancelling a real in-flight run kills the process, ends the stream as `cancelled`, and frees the repo for a new run
- [ ] 16.6 Confirm the dashboard, opened in a real browser during a run, shows it live and then shows it correctly in history afterward, including full output
- [ ] 16.7 Restart the service mid-run and confirm the affected history row shows `Interrupted` while unrelated prior history remains intact
- [ ] 16.8 Run the full install → use → upgrade sequence end-to-end on a clean host and confirm no manual steps beyond running the two scripts were needed
- [ ] 16.9 During a real `dotnet test` run, confirm the dashboard shows live CPU/RAM (host and per-run process tree including `testhost.exe`), and confirm killing `testhost.exe` from the dashboard clears the hang without needing to cancel the whole run
- [ ] 16.10 During and after that same run, confirm its live graph updates while in flight and its full-duration graph is viewable afterward in history
- [ ] 16.11 Confirm `dotnet build` and `dotnet test` (unit + BDD) both pass on a clean checkout via the .NET 10 SDK CLI alone, with no manual setup beyond `dotnet restore`
- [ ] 16.12 Confirm a real sbx-side Claude Code instance, using only the skill from §15, successfully drives a full build/test/cancel cycle against a real deployed instance of the service
