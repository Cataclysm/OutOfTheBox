## Why

This project has no production callers yet — the bearer-token REST+SSE API and the MCP server (`openspec/changes/sbx-mcp-server/`) briefly existed side by side, but keeping a full second interface (and the client-side skill doc explaining how to call it) around purely for its own sake adds ongoing surface area — code, tests, and documentation — for a capability set MCP already covers completely, more cheaply for an MCP-native caller like Claude Code. With no shipped release and no active clients that could break, removing the now-redundant interface now (rather than carrying it indefinitely "just in case") keeps the codebase to exactly the two interfaces this service actually needs: the MCP server for the sbx sandbox caller, and the Blazor Server dashboard for the human operator.

## What Changes

- **BREAKING** (pre-release, no shipped callers): removes the entire bearer-token REST+SSE API — `POST /run`, `POST /run/git`, `POST /run/{runId}/cancel`, `POST /files`, `GET /repositories`, `POST /repositories/clone` — and the `BearerAuthenticationFilter`/`SseWriter`/`SseProcessOutputSink` machinery that backed it. The MCP server (already shipped, additive, in `openspec/changes/sbx-mcp-server/`) is now this service's only sbx-facing interface; every capability the REST API offered remains available through `dotnet_run`/`git_run`/`read_run_output`/`cancel_run`/`transfer_file`/`list_repositories`/`clone_repository`.
- Removes `skills/dotnet-command-service/SKILL.md` (and the `skills/` directory) — MCP tools are self-describing (name, description, and a generated input schema per tool), so there is no longer a hand-written client guide to keep in sync with the real contract the way the REST skill doc needed.
- The Blazor Server dashboard and its cookie-based login are entirely unaffected — it never used the bearer-token REST surface.
- `FileTransferMcpTools.TransferFileAsync`'s result now also surfaces the transfer's run id (`McpTransferFileResult.RunId`), matching every other MCP tool that touches run history — a small, independently-motivated consistency improvement made while porting `RunHistoryPersistence.feature`'s REST-driven steps to MCP tool calls (the test needed a way to look up the persisted row it had just caused).
- BehaviorTests: removes every feature/steps file that existed solely to exercise the REST endpoints (`DotnetCommandExecution`, `GitCommandExecution`, `FileTransfer`, `RepositoryRestEndpoints`, `Cancellation`, `ConcurrencyAndLocking`, `ServiceAuthentication`) and the `SseTestClient` test helper; rewrites the REST-driven steps inside `RepositoryManagement.feature`, `RunHistoryPersistence.feature`, and `HostResourceMonitoring.feature` (otherwise-unrelated features that happened to drive a run via a raw REST call) to use MCP tool calls instead; ports the REST-only concurrency/cross-kind-locking and cancellation coverage those deleted files carried into `McpCommandExecution.feature` as pure-MCP scenarios, so no locking/cancellation behavior loses test coverage.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `dotnet-command-execution`: removes the REST-specific requirements (`POST /run`, SSE streaming, REST-scoped cancellation) - the underlying behavior (spawn `dotnet.exe`, per-repository locking, caller-overridable timeout) is unchanged and now lives entirely under `mcp-command-execution`.
- `git-command-execution`: removes the REST-specific requirements (`POST /run/git`) for the same reason - behavior now lives entirely under `mcp-command-execution`.
- `file-transfer`: removes the REST-specific requirements (`POST /files`, streamed-bytes response) - behavior now lives entirely under `mcp-file-transfer`.
- `repository-management`: two requirements replaced (REMOVED + a same-scope ADDED with a corrected title/body, since both the title and content changed enough that a straight MODIFIED wasn't a clean match) - not the whole capability, since most of it (clone/delete/pull/push/branch-switch/commit-graph/file-browser) is dashboard-only and entirely unaffected: the REST-reachable-subset requirement now says list/clone are MCP-reachable instead, and the clone-cancellation requirement now says `cancel_run` accepts a clone's run id instead of the old REST cancel endpoint's explicit refusal.

`service-authentication` is deliberately **not** listed as modified: its requirements were already worded generically ("any of the service's authenticated endpoints"), not REST-specifically, and remain equally true now that the authenticated endpoint is the MCP route instead - only its Purpose line's example list is stale, corrected directly (not via delta, since a Purpose section can't be changed through a delta once a capability already has one) alongside this change's other doc edits.

## Impact

- Deleted: `src/OutOfTheBox.Presentation/Execution/{RunEndpoints,FileTransferEndpoints,RepositoryEndpoints,SseWriter,SseProcessOutputSink,StartRunRequest,FileTransferRequest,CloneRepositoryRequest}.cs`, `src/OutOfTheBox.Presentation/Authentication/BearerAuthenticationFilter.cs`, `skills/dotnet-command-service/` (whole directory).
- Changed: `src/OutOfTheBox.Host/Program.cs` (three `Map*Endpoints()` calls removed, MCP tool-assembly marker type updated, stale doc comments fixed); `src/OutOfTheBox.Presentation/Mcp/FileTransferMcpTools.cs` (`McpTransferFileResult` gained a `RunId` field).
- Deleted/rewritten BehaviorTests as described in What Changes, above; `tests/OutOfTheBox.BehaviorTests/Support/SseTestClient.cs` deleted (its last consumer went away).
- Documentation: `README.md`, `INSTALL.md`, `CHANGELOG.md` updated to describe the MCP-server-plus-dashboard-only interface shape; `skills/` removed entirely.
- No change to `Domain`, `Infrastructure`, the persistence schema, host/process resource monitoring, or the dashboard itself.
