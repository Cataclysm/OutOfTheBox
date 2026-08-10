## MODIFIED Requirements

### Requirement: No MCP tool exists for any dashboard-only repository action except delete
The system SHALL NOT expose pull/push/force-push/fetch/clean, branch switching, commit checkout, rename, or the file tree browser's rename operation as MCP tools — these remain reachable only through the authenticated dashboard, per `repository-management`'s own dashboard-only boundary for those actions. Repository deletion (`delete_repository`, below) and file/directory deletion within a repository (`delete_path`, per `mcp-file-management`) are no longer in this list.

#### Scenario: No rename/branch-switch/checkout tool is discoverable or callable
- **WHEN** an authenticated caller lists available MCP tools, or attempts to call a tool by any name associated with renaming, pulling, pushing, switching branches, or checking out a commit
- **THEN** no such tool is listed, and any attempt to call one by an assumed name fails as an unknown tool

## ADDED Requirements

### Requirement: delete_repository deletes an entire repository
The system SHALL accept a `delete_repository` tool call carrying a repository name, resolve it under the configured root, and delete that repository's entire directory - the same deletion `repository-management`'s dashboard delete action already performs, including its per-repository locking.

#### Scenario: Deleting an existing repository
- **WHEN** an authenticated caller calls `delete_repository` for a repository that exists under the configured root
- **THEN** the repository's directory is removed from disk and the tool call reports success

#### Scenario: Repository name is invalid, does not exist, or is busy
- **WHEN** an authenticated caller calls `delete_repository` with a name that escapes the configured root, that does not exist under it, or while that repository has another run in flight
- **THEN** the system rejects the call with a confinement, not-found, or busy error respectively, distinguishing all three

#### Scenario: A deletion failure reports why, not just that it failed
- **WHEN** a `delete_repository` call fails after being accepted (e.g. a file locked by another process)
- **THEN** the system reports the underlying error, not just a generic failure

#### Scenario: Every delete_repository call is recorded in run history
- **WHEN** a `delete_repository` call completes, successfully or with an error
- **THEN** it appears in run history with its repository and outcome, the same as a dashboard-triggered deletion already does
