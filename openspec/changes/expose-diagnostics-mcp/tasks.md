## 1. Environment info (Application + Infrastructure)

- [x] 1.1 Add result types in a new `OutOfTheBox.Application.Diagnostics` namespace: `InstalledSdk(string Version, string Path)`, `NuGetSource(string Name, string Url, bool IsEnabled)`, `DiskSpaceInfo(long TotalBytes, long AvailableFreeBytes)`, `EnvironmentInfo(string? DotnetVersion, string? GitVersion, IReadOnlyList<InstalledSdk> InstalledSdks, IReadOnlyList<string> InstalledWorkloadIds, IReadOnlyList<NuGetSource> NuGetSources, DiskSpaceInfo RootDirectoryDiskSpace)`.
- [x] 1.2 Add `IEnvironmentInfoProvider.GetEnvironmentInfoAsync(CancellationToken)` in the same namespace.
- [x] 1.3 Implement `EnvironmentInfoProvider` (`OutOfTheBox.Infrastructure.Diagnostics`): injects `IInstalledToolVersionsProvider` (reused, not re-probed) and `IOptions<ServiceOptions>` (for `RootDirectory`). Spawns `dotnet --list-sdks`, `dotnet workload list`, `dotnet nuget list source` directly via `Process` (matching `InstalledToolVersionsProvider`'s own established pattern, not `IProcessRunner`), parses each per design.md, and reads disk space via `System.IO.DriveInfo` against the drive containing `RootDirectory`. No caching (see design.md) - every field computed fresh per call. Every parse failure degrades to an empty list/null for that field alone, never throwing.
- [x] 1.4 Unit tests for the three output parsers (SDK list, NuGet source list, workload list) against realistic captured sample output strings, including the "No workloads installed" and malformed/unexpected-format cases.

## 2. `get_environment_info` MCP tool (Presentation)

- [x] 2.1 Add `McpEnvironmentInfoResult`-shaped record(s) near `McpToolResults.cs` (or a new file if that grows unwieldy), matching this project's existing MCP result doc-comment style.
- [x] 2.2 Add `EnvironmentInfoMcpTools.cs` (`OutOfTheBox.Presentation.Mcp`), a new `[McpServerToolType]` class (no explicit DI registration needed) exposing `[McpServerTool] GetEnvironmentInfoAsync()` (no parameters), calling `IEnvironmentInfoProvider` and mapping to the MCP result shape. XML doc comments + `[Description]` attributes matching `CommandExecutionMcpTools`'s existing style.

## 3. File-lock diagnostics (Application + Infrastructure)

- [x] 3.1 Add `FileLockApplicationType` enum and `FileLockingProcess(int ProcessId, string ApplicationName, FileLockApplicationType ApplicationType, bool IsRestartable)` in `OutOfTheBox.Application.Diagnostics`.
- [x] 3.2 Add `IFileLockInspector.GetLockingProcessesAsync(string filePath, CancellationToken)` in the same namespace.
- [x] 3.3 Add `RestartManager.cs` (`OutOfTheBox.Infrastructure.Diagnostics`), a `[SupportedOSPlatform("windows")]` static class P/Invoking `rstrtmgr.dll` (`RmStartSession`/`RmRegisterResources`/`RmGetList`/`RmEndSession`) matching `Win32MemoryStatus`'s exact style (`[DllImport]`, `[StructLayout(LayoutKind.Sequential)]` structs for `RM_UNIQUE_PROCESS`/`RM_PROCESS_INFO`, throws with `Marshal.GetLastWin32Error()` on an unexpected failure code), implementing `RmGetList`'s two-call sizing pattern internally. Always calls `RmEndSession` (via `try`/`finally`) even on failure.
- [x] 3.4 Implement `RestartManagerFileLockInspector : IFileLockInspector` (`OutOfTheBox.Infrastructure.Diagnostics`) wrapping the P/Invoke layer, mapping `RM_APP_TYPE`'s raw values to `FileLockApplicationType`.

## 4. `get_file_lock_info` MCP tool (Presentation)

- [x] 4.1 Add `McpFileLockInfoResult`-shaped record(s) near `McpToolResults.cs`.
- [x] 4.2 Add `FileLockDiagnosticsMcpTools.cs` (`OutOfTheBox.Presentation.Mcp`), exposing `[McpServerTool] GetFileLockInfoAsync(string repository, string path)`: same two-level confinement as `FileTransferMcpTools.TransferFileAsync` (copy the pattern exactly), rejects a nonexistent file as not-found, then calls `IFileLockInspector` and maps the result. XML doc comments + `[Description]` attributes matching existing style.

## 5. `mcp-server` spec/test update

- [x] 5.1 Update `tests/OutOfTheBox.BehaviorTests/McpServer.feature`'s "Listing available tools" scenario to the full ten-tool list (adds `get_environment_info`, `get_file_lock_info` - and confirms `get_run_resources` from the prior change is already present).

## 6. Tests

- [ ] 6.1 `mcp-environment-info.feature` and `mcp-file-lock-diagnostics.feature` (`tests/OutOfTheBox.BehaviorTests/`), Gherkin scenarios transcribed from each spec's `#### Scenario:` blocks, against a real running `Host`. The file-lock scenarios need a real locked file - reuse the locking pattern `RepositoryManagementSteps.GivenAFileInsideThatRepositoryIsLockedOpen` already establishes (open a `FileStream` with `FileShare.Read`, no `Delete` flag) rather than inventing a new one.
- [x] 6.2 Run `dotnet test tests/OutOfTheBox.UnitTests` and `tests/OutOfTheBox.ArchitectureTests` (fast suites) after Section 1 and again after Sections 3-4.
- [ ] 6.3 Run the full suite including `tests/OutOfTheBox.BehaviorTests` before the final commit of this change.

## 7. Live verification

- [ ] 7.1 Start the real `Host`, call `get_environment_info` over a raw MCP call, and confirm the returned dotnet/git versions, SDK list, and NuGet sources genuinely match this host's actual installed state (cross-check against `dotnet --list-sdks`/`dotnet nuget list source` run directly in a shell) - not just that the call succeeds structurally.
- [ ] 7.2 Lock a real file open (a second process or an explicit `FileStream`), call `get_file_lock_info` for it over a raw MCP call, and confirm the real locking process id is correctly reported. Confirm an unlocked file returns an empty list, and a nonexistent/escaping path is rejected.
- [ ] 7.3 Confirm `RmEndSession` is genuinely being called even on an error path (no session handle leak) - inspect for repeated calls in a row without failure.

## 8. Wrap-up

- [ ] 8.1 Before the final commit, check `git diff --staged` for leftover debug code, per this repository's standing convention.
- [ ] 8.2 Commit and push. Do not archive this change (or the still-unarchived `expose-run-resources-mcp`) as part of implementation - archiving is a separate, deliberate step.
