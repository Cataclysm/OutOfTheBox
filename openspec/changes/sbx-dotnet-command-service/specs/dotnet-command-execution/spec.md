## Purpose

Lets a remote caller (an sbx sandbox running Claude Code) run `dotnet` CLI commands against a repository checked out on the Windows host, and get back the result, without the sandbox needing a local .NET toolchain.

## ADDED Requirements

### Requirement: Execute posted dotnet command
The system SHALL accept an authenticated HTTP request containing a `dotnet` argument list and a target working directory, invoke `dotnet.exe` with those arguments in that directory, assign the run a unique identifier delivered to the caller before output streaming begins, and deliver the result to the caller.

#### Scenario: Successful build command
- **WHEN** an authenticated caller posts arguments `["build"]` with working directory `myrepo/src`
- **THEN** the system runs `dotnet build` in `myrepo/src`, delivers a run identifier, and delivers exit code, stdout, and stderr captured from the process

#### Scenario: Failing test command
- **WHEN** an authenticated caller posts arguments `["test"]` for a project with failing tests
- **THEN** the system delivers the non-zero exit code produced by `dotnet test` along with its stdout/stderr, without treating the non-zero exit as a transport-level error

### Requirement: Caller may override the execution timeout per request
The system SHALL accept an optional per-request timeout from the caller and SHALL use it in place of the configured default for that run, and SHALL apply the configured default when the caller does not supply one. The system SHALL clamp any caller-supplied timeout to a configured maximum, never permitting an unbounded or excessively long timeout regardless of what the caller requests.

#### Scenario: Caller supplies a timeout
- **WHEN** an authenticated caller starts a command and specifies a timeout shorter than the configured default
- **THEN** the system kills the command if it is still running once that caller-supplied duration elapses, not the default

#### Scenario: Caller omits a timeout
- **WHEN** an authenticated caller starts a command without specifying a timeout
- **THEN** the system applies the configured default timeout

#### Scenario: Caller-supplied timeout exceeds the configured maximum
- **WHEN** an authenticated caller specifies a timeout longer than the configured maximum
- **THEN** the system applies the configured maximum instead, rather than honoring the caller's larger value

### Requirement: Commands against different repos run in parallel
The system SHALL allow commands targeting different repositories (distinct resolved working-directory roots) to execute concurrently, with no serialization between them.

#### Scenario: Two different repos run at the same time
- **WHEN** an authenticated caller starts a command against `repo-a` and, before it finishes, starts a command against `repo-b`
- **THEN** both commands execute concurrently and each completes independently of the other

### Requirement: One in-flight command per repo, shared with git-command-execution
The system SHALL allow at most one `dotnet` command to be in flight at a time for a given repository, SHALL treat a same-repo `git` run (per `git-command-execution`) as contending for that same lock, and SHALL reject a new request targeting a repo that already has an in-flight command of either kind rather than queuing it.

#### Scenario: Second dotnet command for a busy repo is rejected
- **WHEN** an authenticated caller starts a command against `repo-a` and, while it is still running, another authenticated caller starts a second command against `repo-a`
- **THEN** the system rejects the second request with a conflict error identifying the run id of the command already in flight for `repo-a`, and does not invoke a second `dotnet.exe` process for that repo

#### Scenario: A dotnet command is rejected while a git command is in flight for the same repo
- **WHEN** a `git` run is in flight against `repo-a` and an authenticated caller starts a `dotnet` command against `repo-a`
- **THEN** the system rejects the request with a conflict error identifying the in-flight `git` run's id, and does not invoke `dotnet.exe`

#### Scenario: Repo becomes available after completion
- **WHEN** the in-flight command for `repo-a` (of either kind) reaches any terminal state (completed, timed out, or cancelled)
- **THEN** a subsequent request targeting `repo-a` is accepted and executed

### Requirement: Caller can cancel an in-flight command
The system SHALL accept an authenticated cancellation request identifying a run by its run id, and SHALL terminate that run's `dotnet.exe` process if it is still in flight.

#### Scenario: Cancelling a running command
- **WHEN** an authenticated caller cancels the run id of a command that is still in flight
- **THEN** the system terminates the process, the run's output stream ends with a terminal signal distinct from normal completion and identifying the run as cancelled, and the repo's lock is released

#### Scenario: Cancelling an unknown or finished run
- **WHEN** an authenticated caller cancels a run id that does not exist or has already reached a terminal state
- **THEN** the system rejects the cancellation request without affecting any other run

### Requirement: Output is streamed incrementally
The system SHALL deliver stdout and stderr to the caller as the process produces them, rather than withholding all output until the process exits, so a caller observing a long-running command sees progress before completion.

#### Scenario: Output arrives before completion
- **WHEN** a running `dotnet test` command has produced output but has not yet exited
- **THEN** the caller has already received one or more stdout/stderr events for that command before the completion event arrives

#### Scenario: Completion is a distinct, terminal signal
- **WHEN** the process exits with any exit code
- **THEN** the system delivers exactly one terminal event carrying that exit code, distinguishable from stdout/stderr data events, after which no further data events for that command are delivered

### Requirement: Execution is limited to the dotnet CLI
The system SHALL only ever invoke the `dotnet` executable; it SHALL NOT execute arbitrary shell commands, shell operators (pipes, redirects, `&&`), or any executable other than `dotnet.exe`, regardless of what is present in the posted argument list.

#### Scenario: Argument list is passed as discrete process arguments
- **WHEN** a caller posts an argument list containing a value like `; rm -rf /` as a single array element
- **THEN** the system passes it to `dotnet.exe` as one literal argument (not interpreted by a shell) and does not spawn any process other than `dotnet.exe`

### Requirement: Working directory is confined to a configured root
The system SHALL resolve the caller-supplied working directory against a single configured root directory on the host and SHALL reject any request whose resolved path falls outside that root.

#### Scenario: Path escape attempt
- **WHEN** a caller posts a working directory value such as `../../Windows/System32`
- **THEN** the system rejects the request with an error and does not invoke `dotnet.exe`

#### Scenario: Path within root
- **WHEN** a caller posts a working directory that resolves to a subdirectory of the configured root
- **THEN** the system runs the command with that directory as the process working directory

### Requirement: Outcome is reported unambiguously
The system SHALL deliver, for every executed command, the process exit code and the stdout/stderr produced, distinguishing a completed process (any exit code) from a request that could not be executed at all (for example: invalid path, invalid arguments, `dotnet.exe` not found, repo already locked by another run), a run that was terminated before completion by the execution timeout, and a run that was terminated before completion by caller cancellation.

#### Scenario: Execution never starts
- **WHEN** a request fails validation (e.g. path escape, missing arguments) or targets a repo already locked by another in-flight run
- **THEN** the system delivers an error signal that does not contain a process exit code, before any stdout/stderr data is delivered, distinguishable from a completed run

#### Scenario: Execution is terminated by timeout
- **WHEN** a running command is killed by the execution timeout
- **THEN** the system delivers a terminal signal indicating the run timed out, distinguishable from a validation failure, a cancellation, and a completed run with an exit code

#### Scenario: Execution is terminated by cancellation
- **WHEN** a running command is killed because the caller cancelled its run id
- **THEN** the system delivers a terminal signal indicating the run was cancelled, distinguishable from a validation failure, a timeout, and a completed run with an exit code
