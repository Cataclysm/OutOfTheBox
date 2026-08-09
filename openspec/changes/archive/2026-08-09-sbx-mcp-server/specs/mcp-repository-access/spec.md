## Purpose

Lets an MCP caller list and clone repositories — nothing more; every other `repository-management` action (delete, pull/push/force-push/fetch/clean, branch switching, commit checkout, the file tree browser) stays reachable only through the authenticated dashboard.

## ADDED Requirements

### Requirement: list_repositories mirrors the dashboard's repository inventory
The system SHALL accept a `list_repositories` tool call and return every repository under the configured root with the same identifying stats `repository-management` already requires for the dashboard's Repositories view: name, total size, git status summary, and active/idle state.

#### Scenario: Listing repositories
- **WHEN** an authenticated caller calls `list_repositories`
- **THEN** the result lists every repository under the configured root with its name, total size, git status summary, and active/idle state

### Requirement: clone_repository starts a clone as a pollable run
The system SHALL accept a `clone_repository` tool call carrying a source URL, a name, and an optional initial branch, resolve the name under the configured root (rejecting a name that would escape the root or that already exists), and start `git clone` targeting that resolved directory — returning a run id immediately, in the same start-then-poll shape `mcp-command-execution` defines for `dotnet_run`/`git_run`, with progress and completion retrieved via that same capability's `read_run_output` tool.

#### Scenario: Starting a clone
- **WHEN** an authenticated caller calls `clone_repository` with a source URL and a name that does not already exist under the configured root
- **THEN** the tool call returns promptly with a run id, and `git clone` begins executing against the resolved, not-yet-existing target directory

#### Scenario: Name already exists
- **WHEN** an authenticated caller calls `clone_repository` with a name that already exists under the configured root
- **THEN** the system rejects the call without starting a clone

#### Scenario: Reading clone progress and completion
- **WHEN** an authenticated caller calls `read_run_output` for a run id returned by `clone_repository`
- **THEN** it returns the clone's incremental and, once finished, terminal status exactly as it would for a `dotnet_run`/`git_run` run

### Requirement: An in-flight clone is cancellable via cancel_run
`cancel_run` (per `mcp-command-execution`) SHALL accept the id of an in-flight `clone_repository` run and cancel it - one cancellation tool covering every run kind MCP can start.

#### Scenario: Cancelling an in-flight clone
- **WHEN** an authenticated caller calls `cancel_run` with the id of a `clone_repository` run that is still in flight
- **THEN** the system terminates the clone and releases its target directory, and a subsequent `read_run_output` call reports status "cancelled"

### Requirement: No MCP tool exists for delete or any other dashboard-only repository action
The system SHALL NOT expose repository deletion, pull/push/force-push/fetch/clean, branch switching, commit checkout, or the file tree browser's operations as MCP tools — these remain reachable only through the authenticated dashboard, per `repository-management`'s own dashboard-only boundary for those actions.

#### Scenario: No delete tool is discoverable or callable
- **WHEN** an authenticated caller lists available MCP tools, or attempts to call a tool by any name associated with deleting or otherwise mutating a repository beyond cloning it
- **THEN** no such tool is listed, and any attempt to call one by an assumed name fails as an unknown tool
