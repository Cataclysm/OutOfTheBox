## 1. Domain/Application types

- [x] 1.1 Add `RepositoryEntryMatch(string RelativePath, string Name, bool IsDirectory, long? SizeBytes, DateTimeOffset LastModifiedUtc)` in `OutOfTheBox.Domain.Repositories` - `find_files`' own result entry shape, distinct from the existing single-directory-listing `RepositoryFileEntry` (which has no relative-path field, since it only ever lists one directory's immediate children).
- [x] 1.2 Add `RepositoryEntryMetadata(string RelativePath, string Name, bool IsDirectory, long? SizeBytes, FileAttributes Attributes, DateTimeOffset CreatedUtc, DateTimeOffset LastModifiedUtc, DateTimeOffset LastAccessedUtc, string? Owner, bool? IsLocked)` in `OutOfTheBox.Domain.Repositories` - `get_file_info`'s result shape. `IsLocked` stays a plain `bool?` here (not the richer `FileLockingProcess` list `IFileLockInspector` returns) so this stays a pure Domain type with no Application-layer dependency.
- [x] 1.3 Add `RunKind.RepositoryFileDelete` and update its doc comment plus `RunKind.RepositoryDelete`'s (no longer "not reachable via any MCP tool").
- [x] 1.4 Add `IRepositoryFileBrowser.FindEntriesAsync(string repositoryName, string pattern, CancellationToken)` and `GetMetadataAsync(string repositoryName, string relativePath, CancellationToken)`, doc-commented per the existing interface's style. Update the interface's own class-level doc comment (currently "for the dashboard's file tree browser" only).
- [x] 1.5 Add `ServiceOptions.McpMaxFindFilesResults` (default 2000), doc-commented like `McpMaxFileTransferBytes`.

## 2. Packages

- [x] 2.1 Add `Microsoft.Extensions.FileSystemGlobbing` to `Directory.Packages.props` and as a `PackageReference` in `OutOfTheBox.Infrastructure.csproj`.
- [x] 2.2 Confirm `System.Security.AccessControl`'s `FileSystemAclExtensions.GetAccessControl` resolves for `net10.0-windows` without an extra package (Windows-specific reference assembly) - if not, add `System.IO.FileSystem.AccessControl` the same way `System.Diagnostics.PerformanceCounter`/`System.Management` were added for their own Windows-specific BCL surface. (Confirmed: resolves with zero extra package, no `CA1416` warning.)

## 3. Infrastructure: RepositoryFileBrowser

- [x] 3.1 Implement `FindEntriesAsync`: resolve+confine the repository root, recursively enumerate files and directories under it (`Directory.EnumerateFileSystemEntries(..., AllDirectories)`), test each entry's repository-relative path against a `Matcher` built from `pattern` (default `**/*` if empty/whitespace) via `Matcher.Match`, map matches to `RepositoryEntryMatch`, cap at `McpMaxFindFilesResults`. Same per-entry `IOException`/`UnauthorizedAccessException` skip-and-continue `ListDirectoryAsync` already uses for transient enumeration failures.
- [x] 3.2 Implement `GetMetadataAsync`: resolve+confine the path, return `null` if it doesn't exist (mirroring `ResolveConfinedFilePathAsync`'s own null-for-invalid convention), otherwise build a `RepositoryEntryMetadata` from `FileInfo`/`DirectoryInfo`, `FileSystemAclExtensions`, and (files only) `IFileLockInspector.GetLockingProcessesAsync`.
- [x] 3.3 Add `IFileLockInspector` as a new constructor dependency of `RepositoryFileBrowser`.
- [x] 3.4 Give `DeleteAsync` a `RunKind.RepositoryFileDelete` history record (start-then-terminal, matching every other mutating action's `IRunRepository.AddAsync`/`UpdateAsync` plus `IRunEventBus.Publish` pair) - both the dashboard's and MCP's delete calls now get one consistent history trail. `RenameAsync` untouched (out of scope).

## 4. Presentation: MCP tools

- [x] 4.1 Add `FileManagementMcpTools.cs` (`OutOfTheBox.Presentation.Mcp`): `find_files`, `get_file_info`, `delete_path`. Same `[McpServerToolType]`/`[McpServerTool]`/`[Description]`/`McpException` conventions as `FileTransferMcpTools`/`FileLockDiagnosticsMcpTools`. `find_files`'/`get_file_info`'s descriptions explicitly recommend themselves over `git_run` for file-listing/metadata questions (per design.md's "lives in the description text" decision). `delete_path` records its own history the same way `TransferFileAsync` does (via `IRunRepository`/`IRunEventBus`) - actually reuses `RepositoryFileBrowser.DeleteAsync`'s own new recording from 3.4, so no separate recording needed in the tool itself.
- [x] 4.2 Add `delete_repository` to the existing `RepositoryAccessMcpTools.cs`: call `IRepositoryManager.DeleteAsync`, then `IRunRepository.FindByIdAsync(runId)` to surface the real outcome/error (per design.md's "requires a follow-up Run lookup" decision), mapping `Rejected`/a failed `Run` to a specific `McpException`.
- [x] 4.3 Add result record types near `McpToolResults.cs` (or new files if that grows unwieldy) for all four new tools' results.

## 5. Docs

- [x] 5.1 `About.razor`: tool list grows to fourteen entries (add `delete_repository`, `find_files`, `get_file_info`, `delete_path`), update "exposes ten/fourteen tools" wording and the synchronous-vs-start-then-poll/confinement summary paragraph below the list.
- [x] 5.2 `CHANGELOG.md`: new entry under Added.

## 6. Tests

- [x] 6.1 Unit tests (extend `tests/OutOfTheBox.UnitTests/Infrastructure/Repositories/` - check for an existing `RepositoryFileBrowser`-focused test file first, add one if none exists) for `FindEntriesAsync` (recursive match, non-recursive pattern, directories included, cap+truncation, confinement rejection) and `GetMetadataAsync` (file vs directory fields, nonexistent path, confinement rejection) against a real temp directory tree - same style `RepositoryManagerTests`/`WorkingDirectoryResolverTests` already use.
- [x] 6.2 `McpFileManagement.feature` (`tests/OutOfTheBox.BehaviorTests/`) + steps: scenarios transcribed from `mcp-file-management/spec.md`'s `#### Scenario:` blocks, against a real running `Host`.
- [x] 6.3 Add `delete_repository` scenarios to `McpRepositoryAccess.feature`/`McpRepositoryAccessSteps.cs`, transcribed from `mcp-repository-access/spec.md`'s new scenarios.
- [x] 6.4 Update `McpServer.feature`'s "Listing available tools" scenario to the full fourteen-tool list.
- [x] 6.5 Run `dotnet test tests/OutOfTheBox.UnitTests` and `tests/OutOfTheBox.ArchitectureTests` after Sections 1-3, again after Section 4.
- [x] 6.6 Run the full suite including `tests/OutOfTheBox.BehaviorTests` before the final commit.

## 7. Live verification

- [x] 7.1 Start the real `Host`, call `find_files` with a recursive (`**/*.cs`) and a non-recursive (`*.md`) pattern against a real repository, and confirm the results match what's actually on disk (cross-check against a manual directory listing) - not just that the call succeeds structurally.
- [x] 7.2 Call `get_file_info` for a real file and a real directory, confirm every field (including owner and, for a deliberately-locked file, `IsLocked`) matches reality.
- [x] 7.3 Call `delete_path` for a real file and a real directory in a scratch repository, confirm both are actually gone from disk and a history row was recorded for each.
- [x] 7.4 Call `delete_repository` for a real scratch repository, confirm it's actually gone from disk, and confirm a confinement/not-found/busy rejection each produce a distinct, specific error message.
- [x] 7.5 Confirm an invalid/escaping path, a nonexistent target, and a locked-file failure each produce a distinct, actionable error message (not a generic "failed") across all four new tools.

## 8. Wrap-up

- [x] 8.1 Before the final commit, check `git diff --staged` for leftover debug code, per this repository's standing convention.
- [x] 8.2 Commit and push. Do not archive this change as part of implementation - archiving is a separate, deliberate step.
