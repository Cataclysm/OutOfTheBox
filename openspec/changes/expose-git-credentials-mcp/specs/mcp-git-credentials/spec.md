## ADDED Requirements

### Requirement: authorize_git_host stores a PAT for use by git_run/clone_repository
The system SHALL accept an `authorize_git_host` tool call carrying a host and a personal access token - no username - store the credential via the configured git credential helper so that subsequent `git_run`/`clone_repository` calls against an `https://{host}/...` remote authenticate transparently, and verify the credential is actually retrievable before reporting success. The token SHALL be transmitted to the credential helper only via the child process's standard input, never as a command-line argument, and SHALL NOT be logged, persisted by this service, or echoed back in the tool's own result.

#### Scenario: Authorizing a new host
- **WHEN** an authenticated caller calls `authorize_git_host` with a host and a token, and a git credential helper is configured on this system
- **THEN** the credential is stored, a subsequent `git credential fill` for that host confirms it is retrievable, and the tool call reports success

#### Scenario: Re-authorizing an already-authorized host replaces the credential
- **WHEN** an authenticated caller calls `authorize_git_host` for a host that already has a stored credential
- **THEN** the existing credential is replaced with the newly supplied one, not duplicated

#### Scenario: No credential helper is configured
- **WHEN** an authenticated caller calls `authorize_git_host` and this system has no `credential.helper` configured
- **THEN** the system rejects the call with a specific error identifying the missing credential helper, not a generic failure

#### Scenario: The post-write verification fails
- **WHEN** a `git credential approve` call for the supplied host succeeds but a subsequent `git credential fill` for that host does not return a retrievable credential
- **THEN** the system reports that the write could not be verified, rather than reporting success

### Requirement: list_authorized_git_hosts reports configured hosts, their health, and no tokens
The system SHALL accept a `list_authorized_git_hosts` tool call and return every host previously authorized via `authorize_git_host` that has not since been revoked, with when it was authorized and its current health - whether the most recent network-touching git operation against it (pull, push, force-push, fetch, or clone) succeeded or failed for an authentication reason, per this capability's needs-credential tracking. The token itself SHALL NOT appear anywhere in the result, since this service never persists or reads it back after the initial write.

#### Scenario: Listing authorized hosts
- **WHEN** an authenticated caller calls `list_authorized_git_hosts` after authorizing one or more hosts
- **THEN** the result lists each authorized host with its authorization timestamp, its current health, and contains no token value

#### Scenario: An authorized host that has since started failing
- **WHEN** an authenticated caller calls `list_authorized_git_hosts` for a host that was authorized but whose most recent network-touching git operation failed for an authentication reason
- **THEN** the result marks that host as needing attention, distinguishing it from a host that is currently working

#### Scenario: No hosts authorized yet
- **WHEN** an authenticated caller calls `list_authorized_git_hosts` before any host has been authorized
- **THEN** the result is an empty list, not an error

### Requirement: revoke_git_host_authorization removes a stored credential
The system SHALL accept a `revoke_git_host_authorization` tool call carrying a host, remove that host's credential from the git credential helper, and remove this service's own record of it so it no longer appears via `list_authorized_git_hosts`.

#### Scenario: Revoking an authorized host
- **WHEN** an authenticated caller calls `revoke_git_host_authorization` for a host that currently has a stored credential
- **THEN** the credential is removed from the git credential helper, the host no longer appears via `list_authorized_git_hosts`, and the tool call reports success

#### Scenario: Revoking a host that was never authorized
- **WHEN** an authenticated caller calls `revoke_git_host_authorization` for a host with no stored credential
- **THEN** the system reports that there was nothing to revoke, distinguishing this from a genuine failure

### Requirement: Every diagnostic-tool failure reports a specific, actionable reason
The system SHALL NOT report a generic "failed" for any of the three tools in this capability - every rejection SHALL identify which specific condition caused it (missing credential helper, unreachable `git.exe`, verification failure, nothing to revoke) so a caller can correct its next call.

#### Scenario: git.exe is unreachable
- **WHEN** any of the three tools in this capability cannot start `git.exe` at all (e.g. it is not on the service account's PATH)
- **THEN** the system reports that git could not be started, distinguishing this from a credential-specific failure

### Requirement: A git_run/clone_repository failure that looks like an authentication problem names it as one
The system SHALL classify a `git_run`/`clone_repository` failure's captured stderr against the same known authentication-failure patterns this capability's own dashboard integration uses, and when it matches, SHALL append a specific note to the failure identifying it as likely authentication-related and naming `authorize_git_host` as the way to address it - distinguishing a host with no credential on file at all from a host with one that is no longer working (a strong signal it expired or was revoked upstream, surfaced using that host's `list_authorized_git_hosts` timestamp). A failure that does not match a known authentication-failure pattern SHALL be reported exactly as it already is today, with no added note.

#### Scenario: Auth failure against a never-authorized host
- **WHEN** a `git_run`/`clone_repository` call fails with an authentication-failure pattern against a host with no credential ever authorized via `authorize_git_host`
- **THEN** the failure's reported detail notes that no credential is currently authorized for that host and names `authorize_git_host` as the next step

#### Scenario: Auth failure against a previously-authorized host
- **WHEN** a `git_run`/`clone_repository` call fails with an authentication-failure pattern against a host that has a credential on file via `authorize_git_host`
- **THEN** the failure's reported detail notes that a credential was previously authorized for that host (and when) but is no longer working, and names `authorize_git_host` as the way to replace it

#### Scenario: A non-authentication failure is unaffected
- **WHEN** a `git_run`/`clone_repository` call fails for a reason that does not match a known authentication-failure pattern
- **THEN** the failure is reported exactly as it would be without this capability installed, with no authentication-related note appended
