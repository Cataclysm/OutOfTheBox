## Context

See `proposal.md` for motivation. Relevant existing state this design builds on:

- `IInstalledToolVersionsProvider`/`InstalledToolVersionsProvider` (`OutOfTheBox.Application`/`Infrastructure.Execution`) already probes `dotnet --version`/`git --version` by spawning `Process` directly (not `IProcessRunner` - that port is for caller-facing, run-tracked, per-repository-locked execution, which none of this is), caching the result for the service's lifetime via `Lazy<Task<T>>` since installed tool versions don't change without a restart.
- `Win32MemoryStatus` (`OutOfTheBox.Infrastructure.Monitoring`) is this codebase's only existing P/Invoke surface: classic `[DllImport]` (not the source-generated `[LibraryImport]`), a `[StructLayout(LayoutKind.Sequential)]` struct, `[SupportedOSPlatform("windows")]` on the class, a clean typed static method wrapping the raw call, throwing with `Marshal.GetLastWin32Error()` on failure.
- `FileTransferMcpTools.TransferFileAsync` establishes the two-level path confinement pattern every repository-relative-file MCP tool uses: `IWorkingDirectoryResolver.Resolve(repository)` then `.ResolveWithinRoot(repositoryRoot, path)`, rejecting either failure with a distinct `McpException` message, and rejecting a target that doesn't exist as a separate, clearly-worded case.
- `get_run_resources` (the immediately preceding change) established this codebase's precedent for a read-only MCP diagnostic tool that creates no persisted `Run`/history record - both new tools here follow the same shape.

## Goals / Non-Goals

**Goals:**
- Give the sbx-side caller enough environment/toolchain visibility to distinguish "this restore failed because the project needs something this host doesn't have" from "this is a real code/config problem."
- Give it a way to identify a file-lock holder without needing an operator to check manually - the single most Windows-specific build-failure pitfall this project's stated purpose calls out.
- Add zero new background work and no persisted state for either tool.

**Non-Goals:**
- Not adding a way to *install* a missing SDK/workload or *release* a file lock - both tools are read-only diagnostics; acting on what they report is left to the caller (e.g. asking the operator, or simply retrying once a lock clears).
- Not attempting to enumerate *every* file a process has open (the reverse direction, "what does process X have locked") - only "what has *this specific file* locked," which is what the stated pitfall (a build failure naming one specific file) actually needs.
- Not using `NtQuerySystemInformation`/`SystemHandleInformation` (the lower-level alternative some tools like Sysinternals' Handle use) - undocumented, unstable across Windows versions, and needs the file's system-level handle path plus per-handle `DuplicateHandle`/`NtQueryObject` calls to resolve a name, an order of magnitude more P/Invoke surface and risk than Restart Manager for the same practical answer.

## Decisions

### File-lock detection: the Restart Manager API (`rstrtmgr.dll`), not a test-open or a third-party tool
Three approaches were available.

1. **Restart Manager (chosen).** `RmStartSession` → `RmRegisterResources` (with the one file path) → `RmGetList` → `RmEndSession` is the actual OS-native mechanism for exactly this question - the same API behind Windows Explorer's own "this file is open in..." dialog and every MSI installer's file-in-use prompt. It returns, per locking process: process id, process start time (for the same PID-reuse-safety reasoning `IProcessMonitor.KillAsync` already applies elsewhere in this codebase), a Restart-Manager-computed application name, and whether RM considers the process safely restartable. No new process is spawned; it's a direct kernel-backed query.
2. **Attempt to open the file exclusively and catch the resulting `IOException` (rejected).** Tells you *that* something has the file locked, never *what* - the caller would learn nothing beyond what a failed build already told it.
3. **Shell out to a third-party tool (e.g. Sysinternals Handle) (rejected).** Would add an external binary dependency this project has deliberately avoided everywhere else (the MCP SDK and vendored Chart.js are the only two non-BCL dependencies of any kind already present), plus licensing/distribution concerns for something the OS already exposes natively.

### `RmGetList`'s two-call sizing pattern
`RmGetList` cannot be called with a pre-guessed buffer size - the correct pattern (documented Win32 usage) is to call it once with a zero-length array to receive `pnProcInfoNeeded` (the call returns `ERROR_MORE_DATA`, not a failure), then call it again with an array of exactly that size. Implemented as a small private retry inside the Infrastructure wrapper, invisible to callers - `IFileLockInspector.GetLockingProcessesAsync` returns a plain `IReadOnlyList<T>` either way.

### New `OutOfTheBox.Application.Diagnostics` namespace, not an extension of `Execution`/`Monitoring`
`IEnvironmentInfoProvider` and `IFileLockInspector` are both "ask the host something diagnostic," a distinct concern from `Execution` (spawning caller-facing commands) and `Monitoring` (live resource sampling) - forcing either into an existing namespace would blur what's already a clean per-concern split (`Concurrency`/`Configuration`/`Events`/`Execution`/`Monitoring`/`Persistence`/`Repositories`). Both new tool result/port types live here; the file-lock P/Invoke wrapper itself lives in `Infrastructure.Diagnostics`, mirroring `Infrastructure.Monitoring`'s own split between the port-implementing class and its low-level Win32 wrapper (`ProcessMonitor` vs. `WmiProcessTree`/`Win32MemoryStatus`).

### `get_environment_info` computes fresh every call - no caching
Unlike `InstalledToolVersionsProvider`'s lifetime-cached dotnet/git version probe, this tool's other fields (SDK list, NuGet sources, disk space) can genuinely change while the service keeps running (an operator installs a workload, adds a NuGet feed, or disk fills up) - caching any of it risks reporting stale environment state to exactly the caller trying to diagnose a *current* environment problem. The tool still reuses the *already-cached* dotnet/git versions from the existing provider (those genuinely don't change without a restart, and re-probing them here would just be redundant process spawns), but computes everything else itself, every call. This is a diagnostic tool invoked occasionally while debugging a failure, not a hot polling path like `read_run_output` - the extra process-spawn cost per call (a few `dotnet` subcommand invocations) is an acceptable trade for always-current accuracy.

### Workload listing is best-effort, parsed conservatively
`dotnet workload list`'s tabular output format is not a stable, documented contract - it has changed shape across SDK versions and prints a plain sentence ("No workloads installed...") when nothing is installed rather than an empty table. The parser skips the header and separator lines and takes the first whitespace-delimited token of each remaining line as a workload id, and any failure to start the process, non-zero exit, or output that doesn't look like a data row simply yields an empty list - never an exception that would fail the whole `get_environment_info` call over what is, for this project's actual WPF-focused use case, the least essential of its four fields (WPF itself needs no separate workload, unlike e.g. MAUI/Android).

## Risks / Trade-offs

- **[Risk]** P/Invoke struct marshaling errors (wrong `[MarshalAs]`/field order/`CharSet`) fail silently or corrupt memory rather than throwing a clear .NET exception. → Mitigation: struct layouts are taken directly from the documented Win32 `RM_UNIQUE_PROCESS`/`RM_PROCESS_INFO` definitions field-for-field, and tasks.md requires live verification against a real locked file (not just a passing unit test with a fake port) before this is considered done - the same "verify live, don't just trust a clean build" discipline already applied to `get_run_resources`.
- **[Risk]** `dotnet workload list`'s output format could change again in a future SDK and silently degrade to an empty list. → Accepted: per the Decisions above, this is explicitly best-effort and the least essential field; a live check against the SDK actually installed on this host (tasks.md) confirms today's format parses correctly, and a future SDK upgrade breaking it fails safe (empty list), not with an error.
- **[Trade-off]** `get_environment_info` takes noticeably longer than a typical MCP call (several sequential `dotnet` subcommand spawns, each with its own CLI startup cost) - accepted as appropriate for an occasional diagnostic tool, not a hot path.

## Migration Plan

Purely additive - two new MCP tools, two new Application ports, no schema/config changes, no impact on any existing tool or the dashboard. Implement behind the existing `dotnet build`/`dotnet test` gates, verify live against a real running `Host` (a real locked file for `get_file_lock_info`, this host's actual installed toolchain for `get_environment_info` - see tasks.md), ship in the next installer build. No feature flag - reachability is already gated by the same bearer token every other MCP tool requires.
