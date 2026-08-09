## Purpose

Prevents any network caller other than the trusted sbx sandbox from invoking the service's MCP server on the Windows host — `dotnet` command execution, `git` command execution, file transfer, and repository list/clone (per `mcp-command-execution`/`mcp-file-transfer`/`mcp-repository-access`) — since the service exposes it to a remote, network-reachable caller. (Originally written against a REST+SSE API, since removed - see `openspec/changes/sbx-remove-rest-api/` - the requirements below were already worded generically enough that only this line needed correcting.)

## ADDED Requirements

### Requirement: Every execution request requires a valid credential
The system SHALL require a bearer/API-key credential on every request to any of the service's authenticated endpoints (`dotnet` command execution, `git` command execution, file transfer, cancellation) and SHALL reject any request missing that credential before taking any other action.

#### Scenario: Missing credential
- **WHEN** a request to any authenticated endpoint arrives without a credential
- **THEN** the system responds with an authentication error and does not invoke `dotnet.exe`/`git.exe` or transfer any file

#### Scenario: Valid credential
- **WHEN** a request includes a credential matching the configured value
- **THEN** the system proceeds to process the request

### Requirement: Invalid credentials are rejected
The system SHALL reject requests whose credential does not match the configured value, using a comparison that does not leak timing information usable to guess the correct credential.

#### Scenario: Wrong credential
- **WHEN** a request includes a credential that does not match the configured value
- **THEN** the system responds with an authentication error and does not invoke `dotnet.exe`/`git.exe` or transfer any file

### Requirement: Credential is configured out of band
The system SHALL read its accepted credential from host configuration (for example, environment variable or configuration file) rather than embedding it in source code, so the credential can be rotated without a code change.

#### Scenario: Credential rotation
- **WHEN** the operator changes the configured credential value and restarts the service
- **THEN** only requests using the new credential are accepted
