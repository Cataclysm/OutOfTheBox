## MODIFIED Requirements

### Requirement: list_repositories mirrors the dashboard's repository inventory
The system SHALL accept a `list_repositories` tool call and return every repository under the configured root with the same identifying stats `repository-management` already requires for the dashboard's Repositories view: name, total size, git status summary, active/idle state, and whether its remote host currently appears to need a working credential.

#### Scenario: Listing repositories
- **WHEN** an authenticated caller calls `list_repositories`
- **THEN** the result lists every repository under the configured root with its name, total size, git status summary, active/idle state, and needs-credential state
