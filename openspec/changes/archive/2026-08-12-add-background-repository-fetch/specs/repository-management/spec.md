## ADDED Requirements

### Requirement: Every repository is fetched automatically in the background

The system SHALL run `git fetch` against every git repository under the configured root on a regular background cadence (default 300 seconds, configurable), independent of and much slower than the git-status/size cadences described in "Repository stats update on two independent cadences" - those stay purely local and are unaffected by this requirement. The first cycle SHALL wait until the host has fully finished starting (every hosted service's own startup work complete, accepting connections) before running, rather than running any earlier during startup itself - once past that point, the first cycle SHALL run immediately rather than waiting a full background-fetch interval for its first one. This background fetch SHALL use the same per-repository action as an operator-triggered fetch (the same lock, credential-outcome recording, and stats refresh), and a repository already busy with another run SHALL be skipped for that cycle rather than queued or retried early. A single repository's fetch failing, for any reason, SHALL NOT stop the cycle for any other repository, nor stop future cycles from running.

#### Scenario: A repository is fetched without any operator or MCP action
- **WHEN** a repository has had no pull/push/fetch/clone triggered against it, and the service has fully finished starting
- **THEN** the system has run `git fetch` against it at least once shortly after startup, without waiting a full background-fetch interval, and its ahead/behind/remote-gone state reflects that fetch

#### Scenario: The first cycle waits for the host to finish starting
- **WHEN** the host is still in the middle of starting up (migrations, certificate loading, other hosted services' own startup work)
- **THEN** no background fetch cycle runs yet - the first one waits until the host has fully started

#### Scenario: A busy repository is skipped, not queued
- **WHEN** a background fetch cycle reaches a repository that currently holds its per-repository command lock (an in-flight run)
- **THEN** the system skips that repository for this cycle without waiting for it to become idle or queuing a fetch to run immediately after

#### Scenario: A repository's fetch failure does not stop the cycle
- **WHEN** the background fetch against one repository fails (including for an authentication reason)
- **THEN** the system continues the cycle for every other repository, and the failure is recorded against that repository the same way an operator-triggered fetch's failure would be
