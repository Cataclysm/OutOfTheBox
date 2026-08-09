## REMOVED Requirements

### Requirement: An in-flight clone can be cancelled from the dashboard, not the REST cancel endpoint
**Reason**: The REST cancel endpoint this requirement's title and body refer to has been removed entirely - the requirement's *dashboard*-cancellation half is unchanged, but its title/body specifically call out and explain the REST endpoint's own refusal behavior, which no longer exists to refuse anything.
**Migration**: See the replacement requirement "An in-flight clone can be cancelled from the dashboard or via cancel_run", added below - dashboard cancellation is identical; REST cancellation is replaced by `cancel_run` (per `mcp-command-execution`), which deliberately *does* accept a clone's run id (the opposite of the old REST endpoint's behavior).

### Requirement: Deletion and the new git operations are reachable only from the authenticated dashboard; listing and cloning are also REST-reachable
**Reason**: The REST API this requirement describes has been removed entirely.
**Migration**: See the replacement requirement "Deletion and the new git operations are reachable only from the authenticated dashboard; listing and cloning are also MCP-reachable", added below.

## ADDED Requirements

### Requirement: An in-flight clone can be cancelled from the dashboard or via cancel_run
The system SHALL let an operator cancel an in-flight repository clone from the dashboard, and SHALL accept a repository-clone run's id on the `cancel_run` MCP tool (per `mcp-command-execution`) - unlike the REST cancel endpoint this service originally shipped (since removed), which refused a repository-management run's id entirely, `cancel_run` deliberately accepts one, per `mcp-repository-access`'s "An in-flight clone is accepted by cancel_run" requirement and design.md's "one shared cancel_run" decision.

#### Scenario: Cancelling a clone from the dashboard
- **WHEN** an operator cancels an in-flight clone from the Repositories or Status view
- **THEN** the system stops the clone, its history record reflects cancellation, and its lock is released

#### Scenario: Cancelling a clone via the cancel_run MCP tool
- **WHEN** an authenticated MCP caller calls `cancel_run` naming a repository clone's run id
- **THEN** the system stops the clone, its history record reflects cancellation, and its lock is released - the same outcome the dashboard cancellation scenario above produces

### Requirement: Deletion and the new git operations are reachable only from the authenticated dashboard; listing and cloning are also MCP-reachable
The system SHALL expose repository listing and cloning as MCP tools (`list_repositories`, `clone_repository`, per `mcp-repository-access`) reachable by the sbx sandbox caller. Deletion, the pull/push/force-push/fetch/clean/branch-switch actions, the commit graph and commit checkout, and the file tree browser's download/rename/delete SHALL NOT be exposed as MCP tools - they remain available only as authenticated in-process operations (or, for file download specifically, a dashboard-cookie-authenticated endpoint distinct from the bearer-token MCP surface) within the Blazor dashboard, gated by the same dashboard authentication as everything else in `service-dashboard`. This keeps every irreversible or history-rewriting action (delete, force-push, clean, file delete, commit checkout) behind a human operator's explicit dashboard confirmation, unreachable to an sbx caller that "might misuse it."

#### Scenario: List and clone are MCP-reachable
- **WHEN** an authenticated MCP caller calls `list_repositories` or `clone_repository`
- **THEN** the system lists repositories or starts a clone, the same as the equivalent dashboard action would

#### Scenario: No MCP tool exists for delete or the new git operations
- **WHEN** an authenticated MCP caller lists available tools, or attempts to call one by an assumed name for deleting, pulling, pushing, force-pushing, fetching, cleaning, or switching a repository's branch
- **THEN** no such tool is listed, and any attempt to call one by an assumed name fails as an unknown tool - those actions exist only inside the authenticated dashboard
