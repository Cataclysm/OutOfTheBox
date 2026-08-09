## Purpose

Lets an MCP caller identify which process(es) hold a specific file open on this host, to diagnose a Windows "file in use" build/test failure - a pitfall with no Linux analogue the sbx-side caller would already know to suspect.

## ADDED Requirements

### Requirement: Caller can query which processes have a file open
The system SHALL accept a `get_file_lock_info` tool call carrying a repository name and a file path relative to it, resolved with the same two-level path confinement (root→repository, then repository→file) `transfer_file` already applies, and SHALL return the process id, reported application name, and restartability of every process currently holding that file open.

#### Scenario: A file locked by another process
- **WHEN** an authenticated caller calls `get_file_lock_info` for a file another process currently has open
- **THEN** the result lists that process, including its process id and the application name the system reports for it

#### Scenario: A file with no lock
- **WHEN** an authenticated caller calls `get_file_lock_info` for a file that exists and is not held open by any process
- **THEN** the result is an empty list, not an error

### Requirement: An invalid target is rejected, not silently reported as unlocked
The system SHALL reject a `get_file_lock_info` call whose path escapes the named repository or whose repository name is invalid, and SHALL reject one naming a file that does not exist, the same way `transfer_file` rejects those same conditions.

#### Scenario: Path escapes the named repository
- **WHEN** an authenticated caller calls `get_file_lock_info` with a path that resolves outside the named repository
- **THEN** the tool call is rejected, and no lock information is returned

#### Scenario: File does not exist
- **WHEN** an authenticated caller calls `get_file_lock_info` naming a file that does not exist in the named repository
- **THEN** the tool call is rejected as not found
