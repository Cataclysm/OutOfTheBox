## REMOVED Requirements

### Requirement: Execute posted dotnet command
**Reason**: This was the REST endpoint's own contract (`POST /run`, an authenticated HTTP request carrying an argument list and working directory). The REST+SSE API has been removed entirely - no shipped callers existed yet, so this is a clean removal, not a deprecation.
**Migration**: Use the `dotnet_run` MCP tool instead, per `mcp-command-execution`'s "Starting a command returns immediately with a run id" requirement - same argument list/working directory shape, different transport.

### Requirement: Caller may override the execution timeout per request
**Reason**: Specified in terms of the REST request/response shape.
**Migration**: `dotnet_run`'s `timeoutSeconds` parameter, per `mcp-command-execution`'s "Caller may override the execution timeout per call" requirement - identical clamping behavior.

### Requirement: Commands against different repositories run in parallel
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior, now specified under `mcp-command-execution`'s shared `RunRegistry` locking requirements.

### Requirement: One in-flight command per repository, shared with git-command-execution
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior (now shared bidirectionally between `dotnet_run`/`git_run`/`clone_repository`), specified under `mcp-command-execution`'s "One in-flight command per repository" requirement.

### Requirement: Caller can cancel an in-flight command
**Reason**: Specified against the REST `POST /run/{runId}/cancel` endpoint.
**Migration**: Use the `cancel_run` MCP tool, per `mcp-command-execution`'s "Caller can cancel an in-flight run by its id" requirement.

### Requirement: Output is streamed incrementally
**Reason**: This requirement described Server-Sent Events specifically, which no longer exists - MCP tool calls are fundamentally request/response, not a persistent stream.
**Migration**: Poll the `read_run_output` MCP tool instead, per `mcp-command-execution`'s "Incremental and terminal output is retrieved by polling a run id" requirement - the same incremental-visibility property, delivered differently.

### Requirement: Execution is limited to the dotnet CLI
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior - `dotnet_run` always invokes `dotnet.exe`, never an arbitrary executable, the same guarantee the REST endpoint made.

### Requirement: Working directory is confined to a configured root
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior, applied identically to `dotnet_run`'s `workingDirectory` parameter.

### Requirement: Outcome is reported unambiguously
**Reason**: Specified in terms of the REST SSE `done`/`error` event shape.
**Migration**: `read_run_output`'s `status`/`exitCode` fields report the same distinctions (completed/timed out/cancelled/failed-to-start), per `mcp-command-execution`'s own status vocabulary.
