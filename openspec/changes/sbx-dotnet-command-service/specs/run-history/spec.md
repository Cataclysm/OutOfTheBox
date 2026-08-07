## Purpose

Durably records every run the service executes — `dotnet` commands, `git` commands, and artifact transfers alike — including its repo, kind, kind-specific detail (arguments, or a transferred file's path/size), timing, outcome, and full output where applicable, so that both "what is running right now" and "what happened in the past" can be answered after the fact, even across a service restart.

## ADDED Requirements

### Requirement: A run is recorded at start, before completion
The system SHALL create a durable record for a run at the moment it is accepted and begins (repo lock acquired for a command run; transfer begins for an artifact transfer), before the run reaches any terminal state, so an in-flight run is discoverable in the store while it is still running.

#### Scenario: In-flight run is visible in the store
- **WHEN** a run has started but not yet finished
- **THEN** a record for that run exists in durable storage with its run id, repo, kind, start time, and an in-flight outcome

### Requirement: A run's kind is recorded and distinguishable
The system SHALL record whether a run is a `dotnet` command, a `git` command, or an artifact transfer, and SHALL include this kind in both the summary and full record of every run.

#### Scenario: Kind is present on every record
- **WHEN** a caller retrieves a run's summary or full record
- **THEN** the record identifies which of the three kinds it is

### Requirement: A run's terminal outcome and output are persisted
The system SHALL update a run's durable record when it reaches a terminal state (completed, timed out, cancelled, not found, or validation failed) to include its end time and outcome, and — when applicable to that run's kind — its exit code and captured stdout/stderr (for `dotnet`/`git` runs) or its transferred file size (for an artifact transfer).

#### Scenario: Completed run is persisted with output
- **WHEN** a `dotnet` or `git` run finishes with an exit code
- **THEN** the durable record for that run is updated with the end time, the exit code, the outcome, and the stdout/stderr produced by the process

#### Scenario: Completed transfer is persisted with file size
- **WHEN** an artifact transfer finishes successfully
- **THEN** the durable record for that run is updated with the end time, the outcome, and the transferred file's size in bytes

#### Scenario: Persisted record survives a service restart
- **WHEN** the service is restarted after a run has reached a terminal state
- **THEN** that run's record, including its output, remains retrievable from the store

#### Scenario: Interrupted run is reconciled after restart
- **WHEN** the service starts and finds a run record still marked in-flight from before the restart
- **THEN** the system marks that record with an outcome indicating it was interrupted by a restart, rather than leaving it perpetually in-flight

### Requirement: History is queryable
The system SHALL provide a way to list past runs (most recent first) and to retrieve a single run's full record, including its complete stdout/stderr (or transfer metadata), by run id.

#### Scenario: List recent runs
- **WHEN** a caller requests the run list
- **THEN** the system returns runs ordered from most to least recent, including in-flight runs, with enough summary detail (repo, kind, outcome, timestamps, and arguments or transferred path as applicable) to identify each one without fetching full output

#### Scenario: Fetch one run's full detail
- **WHEN** a caller requests a specific run id
- **THEN** the system returns that run's full record including complete stdout/stderr or transfer metadata as applicable, or indicates the run id is unknown

### Requirement: History can be filtered by kind, outcome, and repository
The system SHALL let a caller list runs restricted to any combination of: one or more kinds (`dotnet`, `git`, artifact transfer), one or more outcomes, and a specific repository, returning only runs matching every supplied filter.

#### Scenario: Filter by kind
- **WHEN** a caller lists runs filtered to kind `git`
- **THEN** the system returns only `git` runs, excluding `dotnet` runs and artifact transfers

#### Scenario: Filter by outcome
- **WHEN** a caller lists runs filtered to outcome `TimedOut`
- **THEN** the system returns only runs that timed out

#### Scenario: Combine filters
- **WHEN** a caller lists runs filtered to kind `dotnet` and repository `myrepo`
- **THEN** the system returns only `dotnet` runs against `myrepo`, excluding `dotnet` runs against other repositories and non-`dotnet` runs against `myrepo`

### Requirement: History supports free-text search
The system SHALL let a caller search runs by a free-text query matched against a run's repository, arguments (for `dotnet`/`git` runs), and requested file path (for artifact transfers), and SHALL combine a search query with any active filters.

#### Scenario: Search by repository name
- **WHEN** a caller searches for `myrepo`
- **THEN** the system returns runs whose repository, arguments, or artifact path contain that text

#### Scenario: Search combined with a kind filter
- **WHEN** a caller searches for `reset` while filtered to kind `git`
- **THEN** the system returns only `git` runs whose arguments contain `reset`

### Requirement: A run's resource usage over its lifetime is persisted
The system SHALL persist a time series of each run's aggregate CPU and RAM usage (per `host-resource-monitoring`) from the moment it starts until it reaches a terminal state, at the same sampling cadence used for live monitoring, and SHALL retain the complete series for a completed run regardless of how long the run lasted.

#### Scenario: Full-duration series is retrievable after completion
- **WHEN** an operator requests the resource usage series for a run that has finished
- **THEN** the system returns samples spanning the run's entire duration, from start to its terminal state, not only a recent window

#### Scenario: Series exists for an in-flight run
- **WHEN** an operator requests the resource usage series for a run that is still in flight
- **THEN** the system returns the samples collected so far for that run

### Requirement: Persisted output respects the same size limit as streamed output
The system SHALL persist the same (possibly truncated) stdout/stderr that was streamed to the original caller, and SHALL carry forward the truncation flag, rather than maintaining a separate, larger captured copy purely for history.

#### Scenario: Truncated run's history matches what was streamed
- **WHEN** a run's output exceeded the per-execution output cap and was truncated in the live stream
- **THEN** the persisted record for that run is marked truncated and contains the same truncated content that was streamed, not the full untruncated output
