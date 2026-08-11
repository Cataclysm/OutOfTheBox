## MODIFIED Requirements

### Requirement: Tool discovery lists exactly the tools this service defines
The system SHALL respond to an MCP tool-listing request with exactly the tool set defined by `mcp-command-execution` (`dotnet_run`, `git_run`, `read_run_output`, `cancel_run`), `mcp-file-transfer` (`transfer_file`), `mcp-repository-access` (`list_repositories`, `clone_repository`, `delete_repository`), `mcp-file-management` (`find_files`, `get_file_info`, `delete_path`), `mcp-nuget-credentials` (`authorize_nuget_feed`, `list_authorized_nuget_feeds`, `revoke_nuget_feed_authorization`), `mcp-resource-monitoring` (`get_run_resources`), `mcp-environment-info` (`get_environment_info`), and `mcp-file-lock-diagnostics` (`get_file_lock_info`) - no additional, debug-only, or partially-implemented tools are listed.

#### Scenario: Listing available tools
- **WHEN** an authenticated caller lists available tools
- **THEN** the response contains exactly `dotnet_run`, `git_run`, `read_run_output`, `cancel_run`, `transfer_file`, `list_repositories`, `clone_repository`, `delete_repository`, `find_files`, `get_file_info`, `delete_path`, `authorize_nuget_feed`, `list_authorized_nuget_feeds`, `revoke_nuget_feed_authorization`, `get_run_resources`, `get_environment_info`, and `get_file_lock_info`, each with a description and an input schema sufficient for a caller to construct a valid call without external documentation
