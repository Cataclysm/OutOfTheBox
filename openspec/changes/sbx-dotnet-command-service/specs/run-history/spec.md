## Purpose

Durably records every command the service executes — including its repo, arguments, timing, outcome, and full output — so that both "what is running right now" and "what happened in the past" can be answered after the fact, even across a service restart.

## ADDED Requirements

### Requirement: A run is recorded at start, before completion
The system SHALL create a durable record for a run at the moment its repo lock is acquired and the process is launched, before the run reaches any terminal state, so an in-flight run is discoverable in the store while it is still running.

#### Scenario: In-flight run is visible in the store
- **WHEN** a run has started but not yet finished
- **THEN** a record for that run exists in durable storage with its run id, repo, arguments, start time, and an in-flight outcome

### Requirement: A run's terminal outcome and output are persisted
The system SHALL update a run's durable record when it reaches a terminal state (completed, timed out, or cancelled) to include its end time, outcome, exit code (if any), and the captured stdout/stderr.

#### Scenario: Completed run is persisted with output
- **WHEN** a run finishes with an exit code
- **THEN** the durable record for that run is updated with the end time, the exit code, the outcome, and the stdout/stderr produced by the process

#### Scenario: Persisted record survives a service restart
- **WHEN** the service is restarted after a run has reached a terminal state
- **THEN** that run's record, including its output, remains retrievable from the store

#### Scenario: Interrupted run is reconciled after restart
- **WHEN** the service starts and finds a run record still marked in-flight from before the restart
- **THEN** the system marks that record with an outcome indicating it was interrupted by a restart, rather than leaving it perpetually in-flight

### Requirement: History is queryable
The system SHALL provide a way to list past runs (most recent first) and to retrieve a single run's full record, including its complete stdout/stderr, by run id.

#### Scenario: List recent runs
- **WHEN** a caller requests the run list
- **THEN** the system returns runs ordered from most to least recent, including in-flight runs, with enough summary detail (repo, arguments, outcome, timestamps) to identify each one without fetching full output

#### Scenario: Fetch one run's full detail
- **WHEN** a caller requests a specific run id
- **THEN** the system returns that run's full record including complete stdout/stderr, or indicates the run id is unknown

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
