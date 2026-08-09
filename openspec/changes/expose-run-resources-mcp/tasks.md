## 1. Resource-trend computation (Application)

- [x] 1.1 Add `RunResourceTrend` (`OutOfTheBox.Application.Monitoring`) - a pure static type/method computing `{ LatestCpuPercent, PeakCpuPercent, IdleForSeconds }`-shaped output from an `IReadOnlyList<ResourceHistoryPoint>` and a "now" timestamp, with a fixed low-CPU idle threshold constant (see design.md's "Derived summary" decision).
- [x] 1.2 Unit tests (`tests/OutOfTheBox.UnitTests/Application/Monitoring/`): empty series, single point, idle for the whole window, active at the most recent point, a point exactly at the idle threshold, and points spanning a busy-then-idle transition (idle duration measured from when it actually went idle, not from the window start).

## 2. `get_run_resources` MCP tool (Presentation)

- [x] 2.1 Add result record(s) to `McpToolResults.cs` (or a similarly-named new file if that grows unwieldy) matching this project's existing MCP result style - raw sample points plus the derived summary from Task 1, following `McpReadRunOutputResult`'s doc-comment conventions.
- [x] 2.2 Add `ResourceMonitoringMcpTools.cs` (`OutOfTheBox.Presentation.Mcp`), a new `[McpServerToolType]` class (no explicit DI registration needed - `WithToolsFromAssembly` already scans this assembly) exposing `[McpServerTool] GetRunResourcesAsync(Guid runId)`: look up the run via `IRunRepository.FindByIdAsync` and throw `McpException` for an unknown id (matching `read_run_output`/`cancel_run`'s existing convention exactly), then read `ResourceHistoryBuffer.Get(runId.ToString())` and return its points plus the Task 1 summary (empty/null summary when there are no points, per spec).
- [x] 2.3 XML doc comments + `[Description]` attributes on the tool method and its parameter, matching `CommandExecutionMcpTools`'s existing style (tool discovery is self-describing - no separate skill doc to update).

## 3. Tests

- [ ] 3.1 `mcp-resource-monitoring.feature` (`tests/OutOfTheBox.BehaviorTests/`), Gherkin scenarios transcribed directly from `specs/mcp-resource-monitoring/spec.md`'s `#### Scenario:` blocks, per this repository's established convention (spec and executable test in lockstep). Cover: polling an in-flight run with active samples, an unknown run id being rejected, and a freshly-started run (before the sampler's first tick) returning an empty result rather than an error.
- [ ] 3.2 Run `dotnet test tests/OutOfTheBox.UnitTests` and `tests/OutOfTheBox.ArchitectureTests` (fast suites) after Section 1 and again after Section 2.
- [ ] 3.3 Run the full suite including `tests/OutOfTheBox.BehaviorTests` before the final commit of this change.

## 4. Live verification

- [ ] 4.1 Start the real `Host`, start a real long-enough-running `dotnet_run`/`git_run` via a raw MCP call (or the existing test client pattern), call `get_run_resources` for it while in flight, and confirm the returned points and derived summary reflect real observed CPU/RAM - not just that the call succeeds structurally.
- [ ] 4.2 Confirm an unknown run id is rejected, and that calling immediately after starting a run (before the ~3s sampler tick) returns an empty result rather than an error.
- [ ] 4.3 Confirm the dashboard's own live resource graphs (Status page) are unaffected - same `ResourceHistoryBuffer` instance, now with a second reader.

## 5. Wrap-up

- [ ] 5.1 Before the final commit, check `git diff --staged` for leftover debug code, per this repository's standing convention.
- [ ] 5.2 Commit and push. Do not archive this change as part of implementation - archiving is a separate, deliberate step.
