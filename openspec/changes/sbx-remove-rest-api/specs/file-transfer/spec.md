## REMOVED Requirements

### Requirement: Transfer a file confined to its own repository
**Reason**: This was the REST endpoint's own contract (`POST /files`). The REST+SSE API has been removed entirely - no shipped callers existed yet, so this is a clean removal, not a deprecation.
**Migration**: Use the `transfer_file` MCP tool instead, per `mcp-file-transfer`'s "transfer_file returns a confined file's contents" requirement - same two-level confinement, different transport (a base64-encoded tool result, not a raw byte stream).

### Requirement: Missing file is distinguishable from a confinement violation
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior - `mcp-file-transfer`'s own spec distinguishes the same two error cases.

### Requirement: A run id is assigned before the transfer streams
**Reason**: This specifically described the REST `X-Run-Id` response header, arriving before a streamed body - `transfer_file` is a single synchronous response, not a stream, so there is no "before it starts" moment to assign a header at.
**Migration**: `transfer_file`'s result includes the completed transfer's run id directly, for correlation/debugging - there is nothing to assign it "before," since the whole transfer completes within one tool call.

### Requirement: Caller can cancel an in-flight transfer
**Reason**: `transfer_file` is synchronous (bounded by the configured `McpMaxFileTransferBytes` size cap, per `mcp-file-transfer`'s own requirement) - there is no longer an "in-flight" window a separate cancel call could target.
**Migration**: None - a transfer too large to complete "instantly" is now rejected outright (per `mcp-file-transfer`'s size-limit requirement) rather than started and left cancellable.

### Requirement: A transfer is always bounded, even if the connection dies silently
**Reason**: Described REST-specific connection-health concerns (a dead TCP connection with nothing left to detect it) that don't apply to a single synchronous MCP tool call.
**Migration**: None needed - `transfer_file`'s own request/response lifecycle has no equivalent "silently dead connection" failure mode to bound.

### Requirement: Transfers do not contend for the per-repository command lock
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior - `transfer_file` still does not acquire the `RunRegistry` lock `dotnet_run`/`git_run`/`clone_repository` use.

### Requirement: Transfers are recorded in history like command runs
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior, per `mcp-file-transfer`'s own "Every transfer is recorded in run history" requirement.

### Requirement: A transfer's resource usage is tracked using host-level sampling
**Reason**: This described the REST transfer's own multi-second-or-longer streamed-byte-copy window, long enough to be worth a CPU/RAM resource-sample series. A synchronous, size-capped `transfer_file` call completes too quickly for a resource-sample series to be meaningful.
**Migration**: None - `transfer_file` calls are not tracked in the host resource-sampling series; they remain visible in `run-history` (start time, outcome, file size) without a resource graph.

### Requirement: No directory listing
**Reason**: Behavior itself is unchanged; only the REST framing of this requirement is removed.
**Migration**: Unchanged behavior - `transfer_file` still offers no directory-listing capability.
