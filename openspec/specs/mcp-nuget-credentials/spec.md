# mcp-nuget-credentials Specification

## Purpose
Lets an MCP caller store, list, and revoke PAT-based credentials for a private NuGet feed, so `dotnet_run`'s `restore`/`build`/`test`/`pack`/`push` calls can authenticate against that feed transparently, without operator involvement.

## Requirements
### Requirement: authorize_nuget_feed stores a PAT for use by dotnet_run
The system SHALL accept an `authorize_nuget_feed` tool call carrying a feed URL and a personal access token - no username - classify the feed URL, and store the credential via the mechanism appropriate to that classification (see the following two requirements) so that subsequent `dotnet_run` calls (`restore`, `build`, `test`, `pack`, `push`, or any other subcommand that triggers a restore) authenticate against that feed transparently, and verify the credential is actually retrievable before reporting success. The caller does not choose or need to know which mechanism applies. The token SHALL NOT be transmitted as a command-line argument to any spawned process, and SHALL NOT be logged or echoed back in the tool's own result.

#### Scenario: Re-authorizing an already-authorized feed replaces the credential
- **WHEN** an authenticated caller calls `authorize_nuget_feed` for a feed URL that already has a stored credential
- **THEN** the existing credential is replaced with the newly supplied one, not duplicated, regardless of which mechanism backs it

#### Scenario: The feed URL is not valid
- **WHEN** an authenticated caller calls `authorize_nuget_feed` with a value that is not an absolute `http`/`https` URL
- **THEN** the system rejects the call with a specific error identifying the URL as invalid, and no credential is written anywhere

### Requirement: An Azure DevOps Artifacts feed is authorized via the Azure Artifacts Credential Provider
The system SHALL classify a feed URL whose host is `pkgs.dev.azure.com` or ends with `.pkgs.visualstudio.com` as an Azure DevOps Artifacts feed, and authorize it by durably storing the token (encrypted at rest) and making it available to the Azure Artifacts Credential Provider via its documented non-interactive mechanism on every subsequent `dotnet_run` call, rather than writing it as a plain NuGet configuration password.

#### Scenario: Authorizing a new Azure DevOps Artifacts feed
- **WHEN** an authenticated caller calls `authorize_nuget_feed` with an Azure DevOps Artifacts feed URL and a token, and the Azure Artifacts Credential Provider is installed on this system
- **THEN** the token is stored, a subsequent `dotnet_run` call against a repository configured to use that feed authenticates successfully via the credential provider, and the tool call reports success

#### Scenario: The Azure Artifacts Credential Provider is not installed
- **WHEN** an authenticated caller calls `authorize_nuget_feed` with an Azure DevOps Artifacts feed URL and this system has no Azure Artifacts Credential Provider installed
- **THEN** the system rejects the call with a specific error identifying the missing credential provider, not a generic failure

#### Scenario: The post-write verification fails
- **WHEN** storing the token for an Azure DevOps Artifacts feed succeeds but reading it back does not return the value that was just written
- **THEN** the system reports that the write could not be verified, rather than reporting success

### Requirement: Any other feed is authorized by writing a credentialed NuGet package source
The system SHALL classify any feed URL that is not an Azure DevOps Artifacts feed (per the previous requirement) as a generic feed, and authorize it by writing a credentialed package source into this machine's NuGet configuration, so that subsequent `dotnet_run` calls authenticate against it via NuGet's own standard credential resolution.

#### Scenario: Authorizing a new generic feed
- **WHEN** an authenticated caller calls `authorize_nuget_feed` with a feed URL that is not an Azure DevOps Artifacts feed, and a valid token
- **THEN** the credential is stored as a package source in this machine's NuGet configuration, a subsequent read of that configuration confirms the stored credential is retrievable, and the tool call reports success

#### Scenario: The post-write verification fails
- **WHEN** a write to this machine's NuGet configuration for the supplied feed URL succeeds but a subsequent read back of that configuration does not return the credential that was just written
- **THEN** the system reports that the write could not be verified, rather than reporting success

### Requirement: list_authorized_nuget_feeds reports configured feeds and no tokens
The system SHALL accept a `list_authorized_nuget_feeds` tool call and return every feed URL previously authorized via `authorize_nuget_feed` that has not since been revoked, with when it was authorized. The token itself SHALL NOT appear anywhere in the result, since this service never persists or reads it back after the initial write.

#### Scenario: Listing authorized feeds
- **WHEN** an authenticated caller calls `list_authorized_nuget_feeds` after authorizing one or more feeds
- **THEN** the result lists each authorized feed URL with its authorization timestamp, and contains no token value

#### Scenario: No feeds authorized yet
- **WHEN** an authenticated caller calls `list_authorized_nuget_feeds` before any feed has been authorized
- **THEN** the result is an empty list, not an error

### Requirement: revoke_nuget_feed_authorization removes a stored credential
The system SHALL accept a `revoke_nuget_feed_authorization` tool call carrying a feed URL, remove that feed's stored credential via whichever mechanism backs it (the Azure Artifacts Credential Provider's durable store for an Azure DevOps Artifacts feed, or the NuGet configuration's package source for any other feed), and remove this service's own record of it so it no longer appears via `list_authorized_nuget_feeds`.

#### Scenario: Revoking an authorized feed
- **WHEN** an authenticated caller calls `revoke_nuget_feed_authorization` for a feed URL that currently has a stored credential
- **THEN** the credential is removed via whichever mechanism backs it, the feed no longer appears via `list_authorized_nuget_feeds`, and the tool call reports success

#### Scenario: Revoking a feed that was never authorized
- **WHEN** an authenticated caller calls `revoke_nuget_feed_authorization` for a feed URL with no stored credential
- **THEN** the system reports that there was nothing to revoke, distinguishing this from a genuine failure

### Requirement: Every tool failure in this capability reports a specific, actionable reason
The system SHALL NOT report a generic "failed" for any of the three tools in this capability - every rejection SHALL identify which specific condition caused it (invalid feed URL, the NuGet configuration being unwritable, the Azure Artifacts Credential Provider not being installed, verification failure, nothing to revoke) so a caller can correct its next call.

#### Scenario: The NuGet configuration cannot be written
- **WHEN** any of the three tools in this capability cannot read or write this machine's NuGet configuration for a non-Azure-DevOps feed (e.g. an IO or permission error)
- **THEN** the system reports that the NuGet configuration could not be accessed, distinguishing this from a credential-specific failure
