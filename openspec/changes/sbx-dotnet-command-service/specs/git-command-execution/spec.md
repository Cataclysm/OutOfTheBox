## Purpose

Lets a remote caller (an sbx sandbox running Claude Code) run `git` CLI commands (`pull`, `reset`, `fetch`, `checkout`, etc.) against a repository checked out on the Windows host, so it can manage the state of that checkout without a separate remote-shell mechanism. Mirrors `dotnet-command-execution`'s contract exactly (same auth, streaming, timeout, concurrency, and cancellation model) for a different underlying executable — the two capabilities share the same per-repo lock registry and the same cancellation endpoint, since a `git reset` and a `dotnet build` against the same repo directory must not be allowed to run concurrently either.

## ADDED Requirements

### Requirement: Execute posted git command
The system SHALL accept an authenticated HTTP request containing a `git` argument list and a target working directory, invoke `git.exe` with those arguments in that directory, assign the run a unique identifier delivered to the caller before output streaming begins, and deliver the result to the caller.

#### Scenario: Successful pull command
- **WHEN** an authenticated caller posts arguments `["pull"]` with working directory `myrepo`
- **THEN** the system runs `git pull` in `myrepo`, delivers a run identifier, and delivers exit code, stdout, and stderr captured from the process

#### Scenario: Command with a non-zero exit code
- **WHEN** an authenticated caller posts a `git` command that fails (for example a `merge` with conflicts)
- **THEN** the system delivers the non-zero exit code produced by `git`, along with its stdout/stderr, without treating the non-zero exit as a transport-level error

### Requirement: No git subcommand or flag is restricted
The system SHALL accept any `git` argument list the caller supplies, including destructive or history-rewriting operations (for example `reset --hard`, `clean -fdx`, `push --force`, `checkout -- .`), without maintaining an allowlist or denylist of subcommands or flags — the same unrestricted-passthrough trust model already applied to `dotnet-command-execution`.

#### Scenario: A destructive command is accepted
- **WHEN** an authenticated caller posts arguments `["reset", "--hard", "origin/main"]`
- **THEN** the system executes it exactly as posted, without rejecting or altering the argument list based on its content

### Requirement: Caller may override the execution timeout per request
The system SHALL accept an optional per-request timeout from the caller and SHALL use it in place of the configured default for that run, and SHALL apply the configured default when the caller does not supply one. The system SHALL clamp any caller-supplied timeout to a configured maximum, never permitting an unbounded or excessively long timeout regardless of what the caller requests.

#### Scenario: Caller supplies a timeout
- **WHEN** an authenticated caller starts a `git` command and specifies a timeout shorter than the configured default
- **THEN** the system kills the command if it is still running once that caller-supplied duration elapses, not the default

#### Scenario: Caller omits a timeout
- **WHEN** an authenticated caller starts a `git` command without specifying a timeout
- **THEN** the system applies the configured default timeout

### Requirement: Git commands share the same per-repo concurrency lock as dotnet commands
The system SHALL treat a `git` run and a `dotnet` run against the same resolved repository root as contending for the same lock: at most one of either kind may be in flight for a given repo at a time, and a second request of either kind targeting a busy repo SHALL be rejected with the conflicting run's id rather than queued.

#### Scenario: A git command is rejected while a dotnet command is in flight
- **WHEN** a `dotnet test` run is in flight against `repo-a` and, before it finishes, an authenticated caller starts a `git pull` against `repo-a`
- **THEN** the system rejects the `git pull` request with a conflict error identifying the in-flight `dotnet test` run's id, and does not invoke `git.exe`

#### Scenario: A dotnet command is rejected while a git command is in flight
- **WHEN** a `git checkout` run is in flight against `repo-a` and, before it finishes, an authenticated caller starts a `dotnet build` against `repo-a`
- **THEN** the system rejects the `dotnet build` request with a conflict error identifying the in-flight `git checkout` run's id, and does not invoke `dotnet.exe`

#### Scenario: Commands against different repos still run in parallel
- **WHEN** an authenticated caller starts a `git pull` against `repo-a` and, before it finishes, starts a `dotnet build` against `repo-b`
- **THEN** both commands execute concurrently and each completes independently of the other

### Requirement: Caller can cancel an in-flight git command
The system SHALL accept an authenticated cancellation request identifying a run by its run id — using the same cancellation endpoint used for `dotnet` runs — and SHALL terminate that run's `git.exe` process if it is still in flight.

#### Scenario: Cancelling a running git command
- **WHEN** an authenticated caller cancels the run id of a `git` command that is still in flight
- **THEN** the system terminates the process, the run's output stream ends with a terminal signal distinct from normal completion and identifying the run as cancelled, and the repo's lock is released

### Requirement: Output is streamed incrementally
The system SHALL deliver stdout and stderr to the caller as the `git` process produces them, rather than withholding all output until the process exits.

#### Scenario: Output arrives before completion
- **WHEN** a running `git` command has produced output but has not yet exited
- **THEN** the caller has already received one or more stdout/stderr events for that command before the completion event arrives

### Requirement: Execution is limited to the git CLI
The system SHALL only ever invoke the `git` executable for this endpoint; it SHALL NOT execute arbitrary shell commands, shell operators (pipes, redirects, `&&`), or any executable other than `git.exe`, regardless of what is present in the posted argument list.

#### Scenario: Argument list is passed as discrete process arguments
- **WHEN** a caller posts an argument list containing a value like `; rm -rf /` as a single array element
- **THEN** the system passes it to `git.exe` as one literal argument (not interpreted by a shell) and does not spawn any process other than `git.exe`

### Requirement: Working directory is confined to a configured root
The system SHALL resolve the caller-supplied working directory against the same configured root directory used by `dotnet-command-execution` and SHALL reject any request whose resolved path falls outside that root.

#### Scenario: Path escape attempt
- **WHEN** a caller posts a working directory value such as `../../Windows/System32`
- **THEN** the system rejects the request with an error and does not invoke `git.exe`

### Requirement: Outcome is reported unambiguously
The system SHALL deliver, for every executed `git` command, the process exit code and the stdout/stderr produced, distinguishing a completed process (any exit code) from a request that could not be executed at all, a run terminated by the execution timeout, and a run terminated by caller cancellation — using the same outcome vocabulary as `dotnet-command-execution`.

#### Scenario: Execution never starts
- **WHEN** a request fails validation (e.g. path escape, missing arguments) or targets a repo already locked by another in-flight run of either kind
- **THEN** the system delivers an error signal that does not contain a process exit code, before any stdout/stderr data is delivered

### Requirement: Git runs are recorded in history like dotnet runs
The system SHALL record every `git` run in the same durable history store, at the same points in its lifecycle (start, terminal state), and with the same queryability as `dotnet` runs, per `run-history` — distinguished from a `dotnet` run only by its recorded kind.

#### Scenario: A git run appears in history
- **WHEN** a `git pull` run reaches a terminal state
- **THEN** a history record for that run exists with its repo, arguments, kind (`git`), timestamps, outcome, and captured output, retrievable the same way a `dotnet` run's record is
