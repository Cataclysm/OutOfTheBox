## Why

Two of the three diagnostic gaps identified for a sbx-side Claude Code agent building/testing .NET/WPF projects on this Windows host remain unaddressed (the third, per-run resource/process visibility, already shipped as `get_run_resources`). Restore failures are frequently caused by SDK/workload/feed mismatches the agent can't see from stdout alone, and Windows-specific "file in use" build failures (no Linux analogue) leave the agent no way to identify what's actually holding a file open - both currently force blind retries or escalation to the operator instead of a quick, informed diagnosis.

## What Changes

- Add a `get_environment_info` MCP tool: installed `dotnet`/`git` versions (reusing the existing `IInstalledToolVersionsProvider`), installed .NET SDKs, installed workloads (best-effort), configured NuGet package sources, and disk space on the configured root directory's drive.
- Add a `get_file_lock_info` MCP tool: given a repository-relative file path, returns which process(es) currently have it open, via the Windows Restart Manager API - the OS-native mechanism behind Explorer's own "this file is open in..." dialog.
- Both are read-only, create no persisted run/history record (matching `get_run_resources`'s own precedent), and add no new background work or server-side state.

## Capabilities

### New Capabilities
- `mcp-environment-info`: lets an MCP caller inspect this host's installed .NET/git toolchain, configured NuGet sources, and available disk space, to diagnose restore/build failures caused by an environment mismatch rather than a code problem.
- `mcp-file-lock-diagnostics`: lets an MCP caller identify which process(es) hold a specific file open, to diagnose Windows "file in use" build/test failures.

### Modified Capabilities
- `mcp-server`: "Tool discovery lists exactly the tools this service defines" must include `get_environment_info` and `get_file_lock_info` in its enumerated set (alongside `get_run_resources`, already shipped but not yet reflected in this capability's archived spec).

## Impact

- **Affected code**: two new Presentation-layer MCP tool classes (`EnvironmentInfoMcpTools`, `FileLockDiagnosticsMcpTools`) under `src/OutOfTheBox.Presentation/Mcp/`, auto-discovered by the existing `WithToolsFromAssembly` registration - no `Host/Program.cs` changes needed for tool registration. Two new Application-layer ports (`IEnvironmentInfoProvider`, `IFileLockInspector`) in a new `OutOfTheBox.Application.Diagnostics` namespace, implemented in `OutOfTheBox.Infrastructure` (the latter via a new `rstrtmgr.dll` P/Invoke wrapper, this codebase's second P/Invoke surface after `Win32MemoryStatus`).
- **Reuses**: `IInstalledToolVersionsProvider` (dotnet/git version), `IWorkingDirectoryResolver` (file-lock tool's path confinement, same two-level pattern `transfer_file` uses).
- **No REST/dashboard impact, no schema/migration changes, no new third-party dependencies** (Restart Manager is a built-in Windows API).
