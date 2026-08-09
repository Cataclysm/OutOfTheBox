## Purpose

Lets an MCP caller retrieve a single file's contents from within one specific repository's directory tree, with the same two-level path confinement `file-transfer` already applies to the REST API, returned as an MCP tool result instead of a raw HTTP byte stream.

## ADDED Requirements

### Requirement: transfer_file returns a confined file's contents
The system SHALL accept a `transfer_file` tool call carrying a repository name and a repository-relative file path, resolve that path under the named repository's directory (itself resolved under the configured root), and return the file's contents as the tool result — applying the same two-level confinement `file-transfer` already requires (root→repository, then repository→file), rejecting a path that would escape either boundary.

#### Scenario: Successful transfer
- **WHEN** an authenticated caller calls `transfer_file` for a file that exists within the named repository
- **THEN** the tool call returns that file's full contents

#### Scenario: Path escapes the named repository
- **WHEN** an authenticated caller calls `transfer_file` with a file path that resolves outside the named repository's own directory (including via `..` traversal or a symlink)
- **THEN** the system rejects the call with a confinement error and does not return any file content, distinguishing this from a not-found error

#### Scenario: File does not exist
- **WHEN** an authenticated caller calls `transfer_file` for a path that resolves within the named repository but does not exist
- **THEN** the system rejects the call with a not-found error, distinct from a confinement error

### Requirement: A file exceeding the configured size limit is rejected, not truncated
The system SHALL reject a `transfer_file` call for a file larger than a configured maximum size with a distinct error identifying the limit, rather than returning a truncated or partial result.

#### Scenario: File exceeds the configured limit
- **WHEN** an authenticated caller calls `transfer_file` for a file larger than the configured maximum
- **THEN** the system rejects the call with an error stating the file is too large, and returns no file content

### Requirement: Every transfer is recorded in run history
The system SHALL record every `transfer_file` call in run history (per `run-history`), the same way a REST-initiated file transfer already is.

#### Scenario: A completed transfer appears in history
- **WHEN** a `transfer_file` call completes, successfully or with an error
- **THEN** it appears in run history with its repository, file path, and outcome
