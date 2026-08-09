## 1. Package Setup

- [ ] 1.1 Pin an exact stable `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` version in `Directory.Packages.props` (resolve design.md's "exact version to pin" open question against whatever is current and stable at implementation time)
- [ ] 1.2 Reference `ModelContextProtocol` from `Presentation` and `ModelContextProtocol.AspNetCore` from `Host` only, matching the existing composition-root-only pattern used for Serilog
- [ ] 1.3 Wire a minimal `AddMcpServer().WithHttpTransport()` / `app.MapMcp()` in `Host`'s `Program.cs` with zero tools registered yet, and confirm `dotnet build` + a live `initialize` handshake against the running service succeeds before adding any real tool

## 2. Shared Run-Output Buffering

- [ ] 2.1 Add an offset-based, replayable output buffer per run (bounded by the existing configured output size cap) that both the existing SSE path and the new MCP poll path can read from, per design.md's "one piece that touches shared execution-engine plumbing" note
- [ ] 2.2 Confirm the existing REST/SSE behavior and its existing tests are unaffected by this addition (buffering is additive, not a replacement of the current stdout/stderr delivery path)
- [ ] 2.3 Unit tests: reading from offset 0, reading from a non-zero offset, reading after the run reaches a terminal state (repeatable, doesn't error), reading past the truncation cap

## 3. MCP Server Hosting & Authentication (`mcp-server`)

- [ ] 3.1 Apply the same bearer-token credential check `service-authentication` already uses for the REST API to the MCP endpoint, rejecting an unauthenticated or invalid request before any tool executes or is listed
- [ ] 3.2 Confirm tool discovery (`tools/list`) returns exactly the tool set this change defines, each with a description and input schema, once all tools below are registered
- [ ] 3.3 Confirm an unknown tool name and a schema-invalid tool call both fail without starting a process, touching the filesystem, or acquiring a repository lock
- [ ] 3.4 Unit/behavior tests for 3.1-3.3

## 4. Command Execution Tools (`mcp-command-execution`)

- [ ] 4.1 Implement `dotnet_run`/`git_run` tool handlers in `Presentation`, calling the same `IProcessRunner`/`RunRegistry` ports `RunEndpoints` already calls, returning a run id immediately without waiting for completion
- [ ] 4.2 Implement `read_run_output`, backed by the buffering from Section 2, returning incremental output, current status, and (once terminal) exit code
- [ ] 4.3 Implement `cancel_run`, accepting the id of any in-flight run reachable through this capability (dotnet, git, or - per Section 6 - repository clone), terminating its process and releasing its repository lock
- [ ] 4.4 Confirm the per-repository lock is genuinely shared bidirectionally between MCP-started and REST-started runs (a REST run rejects against an MCP-held lock and vice versa) - this is the highest-risk regression point since it touches the existing shared `RunRegistry`
- [ ] 4.5 Caller-supplied timeout: honored when below the configured maximum, clamped when above it, matching `dotnet-command-execution`'s existing REST behavior
- [ ] 4.6 Unit/behavior tests for every scenario in `specs/mcp-command-execution/spec.md`, including the cross-interface locking scenarios (4.4)

## 5. File Transfer Tool (`mcp-file-transfer`)

- [ ] 5.1 Implement `transfer_file`, reusing the same two-level path confinement (`IWorkingDirectoryResolver.ResolveWithinRoot`, applied twice) `FileTransferEndpoints` already uses, returning file content as a base64-encoded blob
- [ ] 5.2 Add a configured maximum file size, rejecting (not truncating) a call for a larger file, distinct from the not-found and confinement-violation errors
- [ ] 5.3 Record every `transfer_file` call in run history, matching the REST file-transfer endpoint's existing recording
- [ ] 5.4 Unit/behavior tests for every scenario in `specs/mcp-file-transfer/spec.md`, including the confinement-violation-vs-not-found-vs-too-large distinctions

## 6. Repository Access Tools (`mcp-repository-access`)

- [ ] 6.1 Implement `list_repositories`, returning the same stats shape `GET /repositories` already returns
- [ ] 6.2 Implement `clone_repository`, returning a run id in the same start-then-poll shape as `dotnet_run`/`git_run`, rejecting a name that escapes the root or already exists
- [ ] 6.3 Confirm `read_run_output` and `cancel_run` (Section 4) work against a clone's run id, including the deliberate divergence from the REST API's "clone is not REST-cancellable" restriction (design.md's "one shared cancel_run" decision)
- [ ] 6.4 Confirm no MCP tool exists for delete, pull/push/force-push/fetch/clean, branch switch, commit checkout, or file-tree-browser operations - these stay dashboard-only
- [ ] 6.5 Unit/behavior tests for every scenario in `specs/mcp-repository-access/spec.md`

## 7. Architecture & Regression Verification

- [ ] 7.1 Confirm `ArchitectureTests` still passes unmodified - the new MCP tool-handler classes live in `Presentation` and reference only `Application`/`Domain`, no new `Infrastructure` dependency
- [ ] 7.2 Full existing `UnitTests`/`BehaviorTests`/`ArchitectureTests` suite still passes unchanged - this change must not alter any existing REST/SSE behavior
- [ ] 7.3 Live verification: run the real `Host`, connect a real MCP client (ideally an actual Claude Code instance configured against the running service's MCP endpoint, per design.md's risk about unverified real-client behavior) and exercise the full flow - list tools, start a `dotnet build`, poll `read_run_output` to completion, clone a repository, cancel an in-flight run, transfer a file, and confirm an invalid bearer token is rejected

## 8. Documentation

- [ ] 8.1 Add an MCP-calling section to `skills/dotnet-command-service/SKILL.md` (or a second skill file, per design.md's open question - decide during this task) covering: the MCP endpoint, auth header, each tool's purpose, and the start/`read_run_output`-poll/`cancel_run` pattern replacing the curl-background-and-poll pattern for commands and clones
- [ ] 8.2 Update this repository's `README.md`/`CHANGELOG.md` and `openspec/changes/sbx-dotnet-command-service/design.md` (a short cross-reference note, not a rewrite) to mention the MCP interface now exists alongside REST
- [ ] 8.3 Archive this change (`openspec archive sbx-mcp-server`) once implementation and live verification are complete, folding its capability specs into `openspec/specs/`
