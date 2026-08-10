# mcp-server Specification

## Purpose
Hosts a Model Context Protocol server as this service's sbx-facing entry point (alongside the separately-authenticated dashboard), reachable over the network by the sbx sandbox's Claude Code instance as native MCP tools, with tool discovery and authentication as the foundation every tool defined by `mcp-command-execution`, `mcp-file-transfer`, and `mcp-repository-access` builds on.
## Requirements
### Requirement: MCP server is reachable over the Streamable HTTP transport
The system SHALL expose an MCP server reachable over the MCP Streamable HTTP transport at a documented network endpoint, supporting the standard MCP initialization handshake.

#### Scenario: Successful initialization
- **WHEN** a caller completes the MCP `initialize` handshake against the service's MCP endpoint
- **THEN** the server responds with its protocol version and capabilities, and the connection is ready to list and call tools

### Requirement: Every MCP request requires the shared bearer credential
The system SHALL require the shared bearer credential (per `service-authentication`) on every MCP request, and SHALL reject a request presenting no credential or an invalid one before any tool executes.

#### Scenario: Valid bearer token
- **WHEN** a caller presents the configured bearer credential on an MCP request
- **THEN** the request is accepted and processed normally

#### Scenario: Missing or invalid bearer token
- **WHEN** a caller presents no credential, or a credential that does not match the configured value, on an MCP request
- **THEN** the system rejects the request with an authentication error and does not execute a tool, list tools, or reveal any repository or run state

### Requirement: Tool discovery lists exactly the tools this service defines
The system SHALL respond to an MCP tool-listing request with exactly the tool set defined by `mcp-command-execution` (`dotnet_run`, `git_run`, `read_run_output`, `cancel_run`), `mcp-file-transfer` (`transfer_file`), `mcp-repository-access` (`list_repositories`, `clone_repository`, `delete_repository`), `mcp-file-management` (`find_files`, `get_file_info`, `delete_path`), `mcp-resource-monitoring` (`get_run_resources`), `mcp-environment-info` (`get_environment_info`), and `mcp-file-lock-diagnostics` (`get_file_lock_info`) - no additional, debug-only, or partially-implemented tools are listed.

#### Scenario: Listing available tools
- **WHEN** an authenticated caller lists available tools
- **THEN** the response contains exactly `dotnet_run`, `git_run`, `read_run_output`, `cancel_run`, `transfer_file`, `list_repositories`, `clone_repository`, `delete_repository`, `find_files`, `get_file_info`, `delete_path`, `get_run_resources`, `get_environment_info`, and `get_file_lock_info`, each with a description and an input schema sufficient for a caller to construct a valid call without external documentation

### Requirement: A malformed or invalid tool call fails without side effects
The system SHALL reject a tool call whose arguments do not satisfy that tool's schema, or that names an unknown tool, with a structured error, and SHALL NOT start a process, touch the filesystem, or acquire a repository lock while rejecting it.

#### Scenario: Unknown tool name
- **WHEN** an authenticated caller calls a tool name that is not in the discovered tool set
- **THEN** the system returns an error identifying the tool as unknown, and no run is started

#### Scenario: Arguments fail schema validation
- **WHEN** an authenticated caller calls a known tool with arguments missing a required field or of the wrong type
- **THEN** the system returns a validation error describing the problem, and no run is started

