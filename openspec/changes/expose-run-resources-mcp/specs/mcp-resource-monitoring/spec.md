## Purpose

Lets an MCP caller poll a run's recent CPU/RAM trend to judge whether an in-flight `dotnet_run`/`git_run`/`clone_repository` is still making progress or has hung, without needing dashboard access.

## ADDED Requirements

### Requirement: Caller can retrieve a run's recent resource-usage trend by run id
The system SHALL accept a `get_run_resources` tool call carrying a run id and SHALL return that run's recent CPU%/RAM sample history (each point carrying a timestamp, CPU percentage, and resident memory in bytes) covering however much of the trailing window is available, most-recent-last.

#### Scenario: Polling an in-flight run with an active process tree
- **WHEN** an authenticated caller calls `get_run_resources` for a `dotnet_run`/`git_run`/`clone_repository` run that has been executing long enough for at least one sample to have been taken
- **THEN** the tool call returns one or more sample points reflecting that run's actual CPU/RAM usage over the recent trailing window

### Requirement: The result summarizes whether the run currently looks active or idle
The system SHALL derive and return, alongside the raw sample points, the latest known CPU percentage, the peak CPU percentage observed within the returned window, and how long it has been since CPU last exceeded a low activity threshold - so a caller can judge "hung vs. slow" without performing its own time-series analysis.

#### Scenario: A run that has been idle for an extended stretch
- **WHEN** an authenticated caller calls `get_run_resources` for a run whose CPU percentage has stayed near zero for several minutes of the returned window
- **THEN** the result's derived summary reflects a low or zero latest CPU percentage and an idle duration spanning that stretch, distinguishing it from a run that was busy a moment ago

#### Scenario: A run that is actively consuming CPU
- **WHEN** an authenticated caller calls `get_run_resources` for a run whose most recent sample shows substantial CPU usage
- **THEN** the result's derived summary reflects that current activity, with an idle duration of zero or near-zero

### Requirement: A run with no resource data yet returns an empty result, not an error
The system SHALL return an empty (but valid) result - no sample points, a null/absent derived summary - for a known run id that has not yet produced any resource sample, rather than treating the absence of data as a failure.

#### Scenario: Polling immediately after starting a run
- **WHEN** an authenticated caller calls `get_run_resources` immediately after `dotnet_run` returns, before the background sampler's first tick has occurred
- **THEN** the tool call succeeds and returns an empty result rather than an error

#### Scenario: Polling a run kind that produces no process-tree samples
- **WHEN** an authenticated caller calls `get_run_resources` for a run kind that has no process tree of its own to sample
- **THEN** the tool call succeeds and returns whatever figures (if any) are being recorded for that run, or an empty result if none are

### Requirement: An unknown run id is rejected
The system SHALL reject a `get_run_resources` call naming a run id that does not correspond to any run this service has recorded, the same way `read_run_output` and `cancel_run` already reject an unknown run id.

#### Scenario: Calling with a run id that was never issued
- **WHEN** an authenticated caller calls `get_run_resources` with a run id this service has no record of
- **THEN** the tool call is rejected with an error identifying the run id as unknown
