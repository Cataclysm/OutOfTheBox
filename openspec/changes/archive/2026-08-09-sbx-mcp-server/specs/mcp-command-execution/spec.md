## Purpose

Lets an MCP caller run `dotnet` and `git` commands against a repository checked out on the host, with per-repository locking, a caller-overridable timeout, and cancellation - as a start-then-poll pair of tool calls, since MCP tool calls are fundamentally request/response, not a blocking call or a persistent stream. (`dotnet-command-execution`/`git-command-execution` originally described the same guarantees against a REST+SSE API, since removed - see `openspec/changes/sbx-remove-rest-api/` - this is now the only interface for this behavior.)

## ADDED Requirements

### Requirement: Starting a command returns immediately with a run id
The system SHALL accept a `dotnet_run` or `git_run` tool call carrying an argument list and a target working directory, begin executing `dotnet.exe`/`git.exe` with those arguments, and return a result carrying a run id and a status of "running" without waiting for the process to exit.

#### Scenario: Starting a dotnet build
- **WHEN** an authenticated caller calls `dotnet_run` with arguments `["build"]` and working directory `myrepository/src`
- **THEN** the tool call returns promptly with a run id and status "running", and `dotnet build` continues executing in the background against `myrepository/src`

#### Scenario: Starting a git command
- **WHEN** an authenticated caller calls `git_run` with arguments `["status"]` and working directory `myrepository`
- **THEN** the tool call returns promptly with a run id and status "running", the same way `dotnet_run` does, against `git.exe` instead of `dotnet.exe`

### Requirement: Incremental and terminal output is retrieved by polling a run id
The system SHALL accept a `read_run_output` tool call carrying a run id and an offset, and SHALL return whatever stdout/stderr content was produced since that offset, the run's current status (running, completed, timed out, cancelled, or failed-to-start), and, once the run has reached a terminal status, its exit code. A `read_run_output` call for a run that has already reached a terminal status SHALL continue to return that run's final status and remaining output, repeatably.

#### Scenario: Polling a still-running command
- **WHEN** an authenticated caller calls `read_run_output` for an in-flight run's id with offset 0, then calls it again later with the offset the first call returned
- **THEN** each call returns only the output produced since the given offset, plus status "running"

#### Scenario: Polling after completion
- **WHEN** an authenticated caller calls `read_run_output` for a run that has already finished
- **THEN** the call returns the run's terminal status, its exit code, and any output not yet retrieved, and a subsequent call for the same run continues to return the same terminal status and exit code rather than an error

#### Scenario: Output exceeding the configured cap is marked truncated
- **WHEN** a run's combined stdout/stderr exceeds the configured output size cap
- **THEN** `read_run_output` reports the run as truncated once the cap is reached, the same distinction `dotnet-command-execution`'s run history already records

### Requirement: One in-flight command per repository
The system SHALL treat an MCP-started `dotnet_run`/`git_run`/`clone_repository` as contending for the same per-repository lock as every other run kind, and SHALL reject a new run targeting an already-busy repository rather than queuing it, regardless of which of those tools is asking.

#### Scenario: A dotnet_run is rejected while a git_run is in flight for the same repository
- **WHEN** a `git_run` is in flight against `repository-a`, and an authenticated caller calls `dotnet_run` against `repository-a`
- **THEN** the tool call is rejected with a conflict error identifying the in-flight run's id, and no new process is started

#### Scenario: A second dotnet_run for a busy repository is rejected
- **WHEN** a `dotnet_run` is in flight against `repository-a`, and an authenticated caller calls `dotnet_run` again against `repository-a`
- **THEN** the tool call is rejected with a conflict error identifying the in-flight run's id, and no new process is started

### Requirement: Caller may override the execution timeout per call
The system SHALL accept an optional timeout on `dotnet_run`/`git_run`, apply the configured default when omitted, and clamp any caller-supplied value to a configured maximum.

#### Scenario: Caller-supplied timeout is honored
- **WHEN** an authenticated caller calls `dotnet_run` with a timeout shorter than the configured default
- **THEN** the system terminates the run once that duration elapses if it is still running, and `read_run_output` subsequently reports status "timed out"

#### Scenario: Caller-supplied timeout exceeds the configured maximum
- **WHEN** an authenticated caller calls `dotnet_run` or `git_run` with a timeout longer than the configured maximum
- **THEN** the system applies the configured maximum instead

### Requirement: Caller can cancel an in-flight run by its id
The system SHALL accept a `cancel_run` tool call naming a run id and, if that run is still in flight, terminate its process and release the repository lock it held. `cancel_run` SHALL accept the id of any in-flight `dotnet`/`git` run reachable through this capability.

#### Scenario: Cancelling an in-flight run
- **WHEN** an authenticated caller calls `cancel_run` with the id of a run that is still in flight
- **THEN** the system terminates the process, releases the repository's lock, and a subsequent `read_run_output` call for that run reports status "cancelled"

#### Scenario: Cancelling a run that has already finished
- **WHEN** an authenticated caller calls `cancel_run` with the id of a run that already reached a terminal status
- **THEN** the system returns that run's existing terminal status without error, rather than treating the call as a failure
