## REMOVED Requirements

### Requirement: Execute posted git command
**Reason**: This was the REST endpoint's own contract (`POST /run/git`). The REST+SSE API has been removed entirely - no shipped callers existed yet, so this is a clean removal, not a deprecation.
**Migration**: Use the `git_run` MCP tool instead, per `mcp-command-execution`'s "Starting a command returns immediately with a run id" requirement.

### Requirement: No git subcommand or flag is restricted
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior - `git_run` accepts any argument list, exactly like `POST /run/git` did.

### Requirement: Caller may override the execution timeout per request
**Reason**: Specified in terms of the REST request/response shape.
**Migration**: `git_run`'s `timeoutSeconds` parameter, per `mcp-command-execution`'s "Caller may override the execution timeout per call" requirement.

### Requirement: Git commands share the same per-repository concurrency lock as dotnet commands
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior, now specified under `mcp-command-execution`'s shared `RunRegistry` locking requirements (`dotnet_run`/`git_run`/`clone_repository` all share it).

### Requirement: Caller can cancel an in-flight git command
**Reason**: Specified against the REST `POST /run/{runId}/cancel` endpoint.
**Migration**: Use the `cancel_run` MCP tool, per `mcp-command-execution`'s "Caller can cancel an in-flight run by its id" requirement.

### Requirement: Output is streamed incrementally
**Reason**: This requirement described Server-Sent Events specifically, which no longer exists.
**Migration**: Poll the `read_run_output` MCP tool instead, per `mcp-command-execution`'s own requirement.

### Requirement: Execution is limited to the git CLI
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior - `git_run` always invokes `git.exe`.

### Requirement: Working directory is confined to a configured root
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior, applied identically to `git_run`'s `workingDirectory` parameter.

### Requirement: Outcome is reported unambiguously
**Reason**: Specified in terms of the REST SSE `done`/`error` event shape.
**Migration**: `read_run_output`'s `status`/`exitCode` fields, per `mcp-command-execution`'s own status vocabulary.

### Requirement: Git runs are recorded in history like dotnet runs
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior - every `git_run` is recorded in `run-history` exactly like a `dotnet_run`.
