## Why

A sbx-side Claude Code agent driving `dotnet_run`/`git_run` remotely can only see stdout/stderr text and a coarse running/completed/failed status via `read_run_output`. Its single most token-expensive failure mode is not being able to tell "still working" from "hung" (a WPF UI-thread deadlock, a test waiting forever on a mutex, an infinite loop) when a run produces no new output for a long stretch - both cases look identical from output alone, and today the agent's only tool is guessing off a timeout. The dashboard already computes and displays exactly the missing signal (per-run CPU%/RAM over time, via `ResourceHistoryBuffer`) but only for the Blazor dashboard, not MCP.

## What Changes

- Add a `get_run_resources` MCP tool that returns a run's recent CPU%/RAM history (from the already-existing, already-populated `ResourceHistoryBuffer`, keyed by run id) plus a small derived summary (latest CPU%, peak CPU% in the window, and how long it's been since CPU last exceeded an idle threshold), so a caller can judge "hung vs. slow" without doing its own time-series math.
- Read-only for this change: no new capability to kill an individual child process of a run over MCP. `cancel_run` (which ends the *whole* run) already exists; killing one process within a multi-process run tree is deferred as a separate, larger-security-surface change if it turns out to be needed (see design.md's rejected-alternative note).
- No new server-side state, no `Domain`/`Infrastructure` changes: `ResourceHistoryBuffer` (an existing `Application`-layer singleton) already retains a 10-minute rolling window per run id, evicted on that run's terminal event exactly the way the dashboard's own live graphs already consume it.

## Capabilities

### New Capabilities
- `mcp-resource-monitoring`: lets an MCP caller poll a run's recent CPU/RAM trend to distinguish a hung run from a slow-but-working one, mirroring what the dashboard's live resource graphs already show for the same data.

### Modified Capabilities
- `mcp-server`: "Tool discovery lists exactly the tools this service defines" must include `get_run_resources` in its enumerated set - adding a tool is necessarily an observable change to that requirement's own exact-list contract.

## Impact

- **Affected code**: one new file, `src/OutOfTheBox.Presentation/Mcp/ResourceMonitoringMcpTools.cs` (new `[McpServerToolType]` class, structurally parallel to the three existing MCP tool classes), plus its DI registration in `Host/Program.cs`. Consumes the existing `ResourceHistoryBuffer` singleton (`OutOfTheBox.Application.Monitoring`) - already registered, already populated by `HostResourceSamplerService` every `ResourceSamplerIntervalSeconds` (~3s default) for every in-flight run.
- **No REST/dashboard impact**: `ResourceHistoryBuffer` is read-only from this new tool's perspective; nothing about how the dashboard populates or consumes it changes.
- **No new dependencies, no schema/migration changes.**
