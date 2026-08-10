## Why

The sbx-side caller currently has two ways to learn about files in a repository: `transfer_file` (needs an exact path already known) and shelling out to `git` via `git_run` (`git ls-files`, `git status`, etc. - indirect, git-index-relative rather than filesystem-relative, and blind to untracked/ignored files and real filesystem metadata like size/attributes/locks). Finding a file by name or extension, or checking whether a specific file is safe to overwrite, currently has no direct tool at all. Separately, on reflection the original "no delete via MCP" boundary (`mcp-repository-access`'s "No MCP tool exists for delete" requirement) is being reversed by direct instruction - a caller that can already create repositories and files should also be able to clean them up without operator involvement for every deletion.

## What Changes

- Add a `find_files` MCP tool: recursive glob search (`*`, `**`, `?`) for files and directories within a named repository, confined to that repository's own directory tree.
- Add a `get_file_info` MCP tool: rich metadata for one filesystem entry (type, size, attributes, created/modified/accessed dates, owner, and whether it's currently locked) - also confined to the named repository.
- Both new tools' descriptions explicitly steer a caller toward them, not `git_run`, for file-listing/metadata questions - a git-index view answers "what's tracked," not "what's actually on disk right now."
- Add a `delete_path` MCP tool: deletes a file or directory (recursively) within a named repository - the MCP-reachable counterpart to the dashboard's existing file tree browser delete action.
- Add a `delete_repository` MCP tool: deletes an entire repository - the MCP-reachable counterpart to the dashboard's existing repository delete action. Reverses `mcp-repository-access`'s prior "no delete" boundary for this one action only; every other dashboard-only repository action (pull/push/force-push/fetch/clean, branch switching, commit checkout, rename) stays dashboard-only.
- Every new tool applies the same two-level path confinement (root→repository, then repository→sub-path) every other repository-relative-path tool in this service already uses, and every error response carries enough detail (what was rejected and why - confinement, not-found, busy, or the underlying OS error message) for a caller to actually act on it, not just know something failed.

## Capabilities

### New Capabilities
- `mcp-file-management`: lets an MCP caller search for files/directories by glob pattern, inspect one entry's full filesystem metadata, and delete a file or directory - all confined to one named repository, and preferred over `git_run` for file-listing/metadata questions.

### Modified Capabilities
- `mcp-repository-access`: adds `delete_repository`; the "No MCP tool exists for delete" requirement is narrowed to cover only the actions still dashboard-only (pull/push/force-push/fetch/clean, branch switching, commit checkout, rename) - repository deletion itself is no longer one of them.
- `mcp-server`: "Tool discovery lists exactly the tools this service defines" grows to include `find_files`, `get_file_info`, `delete_path`, and `delete_repository` (fourteen tools total).

## Impact

- **Affected code**: `IRepositoryFileBrowser`/`RepositoryFileBrowser` (`OutOfTheBox.Application`/`Infrastructure.Repositories`) gain `FindEntriesAsync`/`GetMetadataAsync`, reusing the existing confinement and `RunRegistry` locking conventions; a new `FileManagementMcpTools.cs` (`OutOfTheBox.Presentation.Mcp`) exposes `find_files`/`get_file_info`/`delete_path`; `RepositoryAccessMcpTools.cs` gains `delete_repository`, calling the existing `IRepositoryManager.DeleteAsync` directly (same method the dashboard's own delete button already calls).
- **New dependency**: `Microsoft.Extensions.FileSystemGlobbing` (official, first-party Microsoft package) for glob matching - `Directory.EnumerateFileSystemEntries`'s own wildcard support is limited to a single path segment and doesn't understand `**`.
- **Reuses**: `IWorkingDirectoryResolver` (confinement), `IFileLockInspector` (the `is-locked` field of `get_file_info`, already built for `get_file_lock_info`), `IRepositoryManager.DeleteAsync`/`IRepositoryFileBrowser.DeleteAsync` (both already exist and are already exercised by the dashboard - `delete_repository`/`delete_path` are new *callers* of existing, tested logic, not new deletion logic).
- **No schema/migration changes.** Docs updated: the About page's MCP tool list/count, and this proposal's own specs.
