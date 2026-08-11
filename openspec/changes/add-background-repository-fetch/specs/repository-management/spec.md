## ADDED Requirements

### Requirement: Every repository is fetched automatically in the background

The system SHALL run `git fetch` against every git repository under the configured root on a regular background cadence (default 300 seconds, configurable), independent of and much slower than the git-status/size cadences described in "Repository stats update on two independent cadences" - those stay purely local and are unaffected by this requirement. This background fetch SHALL use the same per-repository action as an operator-triggered fetch (the same lock, credential-outcome recording, and stats refresh), and a repository already busy with another run SHALL be skipped for that cycle rather than queued or retried early. A single repository's fetch failing, for any reason, SHALL NOT stop the cycle for any other repository, nor stop future cycles from running.

#### Scenario: A repository is fetched without any operator or MCP action
- **WHEN** a repository has had no pull/push/fetch/clone triggered against it since the service started, and at least one background-fetch interval has elapsed
- **THEN** the system has run `git fetch` against it at least once, and its ahead/behind/remote-gone state reflects that fetch

#### Scenario: A busy repository is skipped, not queued
- **WHEN** a background fetch cycle reaches a repository that currently holds its per-repository command lock (an in-flight run)
- **THEN** the system skips that repository for this cycle without waiting for it to become idle or queuing a fetch to run immediately after

#### Scenario: A repository's fetch failure does not stop the cycle
- **WHEN** the background fetch against one repository fails (including for an authentication reason)
- **THEN** the system continues the cycle for every other repository, and the failure is recorded against that repository the same way an operator-triggered fetch's failure would be
