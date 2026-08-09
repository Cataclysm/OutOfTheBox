**Cross-reference**: this proposal frames MCP as purely additive alongside the REST+SSE API - that was true when written, but the REST API was subsequently removed entirely (see `openspec/changes/sbx-remove-rest-api/`, on explicit request, since this project had no shipped callers yet). MCP is now this service's only sbx-facing interface. The rest of this document is left as-written, as the historical record of why MCP was added in the first place.

## Why

The sbx sandbox's Claude Code instance drives this service today by constructing raw `curl` commands, backgrounding them, and polling/parsing Server-Sent Events frames from Bash — a mechanics-heavy pattern that exists only because `skills/dotnet-command-service/SKILL.md` has to compensate for the fact that HTTP+SSE isn't something an LLM agent calls natively. Claude Code speaks MCP (Model Context Protocol) natively for tool integration: exposing the same capability as typed MCP tools would let the sbx-side agent call `dotnet`/`git`/file-transfer/repository actions directly, spending far fewer tokens/turns on protocol mechanics and with far less chance of a malformed shell invocation.

## What Changes

- New MCP server hosted inside the existing `OutOfTheBox.Host` process (same Kestrel instance as the REST API and dashboard), reachable over the MCP Streamable HTTP transport, built on the official `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` SDK (`AddMcpServer().WithHttpTransport()`, `MapMcp()`).
- New MCP tools exposing the same actions the bearer-token REST surface already exposes: `dotnet_run`, `git_run`, `read_run_output`, `cancel_run`, `transfer_file`, `list_repositories`, `clone_repository`.
- MCP authentication reuses the exact same bearer token already configured for the REST API, presented as a standard `Authorization: Bearer` header on the MCP transport's HTTP requests and validated by the same credential check — not MCP's newer OAuth 2.1 authorization framework, which is disproportionate for a private point-to-point sandbox-to-host link (rationale in `design.md`).
- Long-running `dotnet`/`git` commands are deliberately **not** exposed as a single blocking tool call. `dotnet_run`/`git_run` return immediately with a run id (mirroring the REST API's `X-Run-Id` semantics); a new `read_run_output` tool lets the caller poll for incremental stdout/stderr plus current status. This start/poll shape — not MCP progress notifications — is the mechanism relied on for incremental visibility; `design.md` covers why.
- The existing REST+SSE API, its bearer-token auth, its skill doc, and all currently-shipped behavior are left completely unchanged. This is a purely additive new interface, not a replacement or migration — no existing capability's requirements change.
- New Presentation-layer endpoint/tool-mapping additions (structurally parallel to the existing `RunEndpoints`/`FileTransferEndpoints` minimal-API classes), calling the exact same Application-layer ports (`IProcessRunner`, `RunRegistry`, `IRunEventBus`, `IRepositoryManager`, etc.) the REST endpoints already call. No `Infrastructure` changes are required — this is a new consumer of already-existing ports, not new underlying behavior.
- The Claude Code skill (`skills/dotnet-command-service/SKILL.md`) gains a second "how to call this over MCP instead of REST" section once implemented — not created as part of this proposal's scope, called out here so it isn't lost.

## Capabilities

### New Capabilities
- `mcp-server`: hosting the MCP server itself inside `Host` over the Streamable HTTP transport, tool discovery, and bearer-token authentication of MCP requests — the "meta" capability with no REST equivalent.
- `mcp-command-execution`: `dotnet_run`/`git_run`/`read_run_output`/`cancel_run` tools, mirroring `dotnet-command-execution`'s and `git-command-execution`'s per-repository locking, timeout, and cancellation semantics, re-expressed as MCP tool calls instead of REST+SSE. Deliberately one capability covering both `dotnet` and `git` (not split the way the REST capabilities are) since the interesting behavior — locking, the start/poll shape, cancellation — is identical between them; only which executable runs differs.
- `mcp-file-transfer`: a `transfer_file` tool mirroring `file-transfer`'s two-level path confinement, returning file content as an MCP tool result.
- `mcp-repository-access`: `list_repositories`/`clone_repository` tools mirroring exactly the REST-reachable subset of `repository-management` (list + clone only) — delete and every dashboard-only action (pull/push/force-push/fetch/clean, branch switch, commit checkout, the file tree browser) stay out of MCP's reach too, for the same "don't give the sbx caller destructive/history-rewriting capability" reason `repository-management`'s spec already gives for REST.

### Modified Capabilities
(none — this change is purely additive; every existing capability's REST+SSE behavior and requirements are untouched)

## Impact

- New third-party dependency: `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` (official C# SDK), added to `Directory.Packages.props` and referenced from `Presentation` (tool definitions, calling only `Application`-layer ports — no new `Infrastructure` dependency) and `Host` (composition-root wiring: `AddMcpServer()`/`MapMcp()`, alongside the existing minimal-API/Blazor registration). Exact package version to be pinned at implementation time against whatever is current and stable then (the SDK is young and was mid-major-version-transition as of this proposal).
- New network-exposed surface on the same HTTP(S) listener the REST API and dashboard already use: an MCP endpoint (conventionally `/mcp`) that, like the REST API, accepts bearer-token-authenticated requests that can execute `dotnet`/`git` commands and read repository files — the same trust boundary and threat model the REST API already documents in `openspec/changes/sbx-dotnet-command-service/proposal.md`'s Impact section, not a new one.
- `RunRegistry`/`IRunEventBus`-backed output buffering needs to support the new `read_run_output` polling access pattern (read output produced since an offset, for a run that may have started before this reader attached) — today's SSE path only supports a single subscriber that starts. `design.md` covers whether this is a new small buffering component or a reuse of existing history/output capture.
- `skills/dotnet-command-service/SKILL.md` will eventually need an MCP-calling section (out of this proposal's implementation scope, tracked so it isn't forgotten).
- No change to `Domain`, `Infrastructure`, persistence schema, or the dashboard.
