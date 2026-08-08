## Purpose

Lets a remote caller (an sbx sandbox running Claude Code) retrieve any file (a compiled binary, a test-results file, source code, or any other file) confined to a repository on the Windows host, since some sbx-side tooling needs the actual file rather than the command output already streamed by `dotnet-command-execution`. Deliberately not restricted to build-output-shaped paths - the caller may request any file inside the named repository, on direct instruction. The critical property is confinement: a transfer must never be able to read a file from outside the specific repository directory the caller named, regardless of how the requested path is constructed.

## ADDED Requirements

### Requirement: Transfer a file confined to its own repository
The system SHALL accept an authenticated request naming a target repository (resolved the same way as `dotnet-command-execution`'s working directory, confined to the configured root) and a file path relative to that repository, resolve the file path against that specific repository's directory, and reject any request whose resolved file path falls outside that repository's own directory tree — including via `..` traversal, an absolute path, or a symlink/junction that resolves outside it.

#### Scenario: Requested file is within the named repository
- **WHEN** an authenticated caller requests path `bin/Debug/net10.0/MyApp.dll` within repository `myrepository`
- **THEN** the system streams that file's contents back to the caller

#### Scenario: Path escapes the named repository via traversal
- **WHEN** an authenticated caller requests path `../other-repository/secret.txt` within repository `myrepository`
- **THEN** the system rejects the request with an error and does not read or transfer any file

#### Scenario: Path escapes via a symlink
- **WHEN** the resolved file path is a symlink or junction whose final target falls outside the named repository's directory
- **THEN** the system rejects the request with an error and does not transfer the symlink's target content

#### Scenario: Requested repository itself is outside the configured root
- **WHEN** an authenticated caller names a repository that resolves outside the configured root directory
- **THEN** the system rejects the request the same way `dotnet-command-execution` rejects an escaping working directory, and does not attempt to resolve any file path

### Requirement: Missing file is distinguishable from a confinement violation
The system SHALL respond with a distinct not-found signal when the resolved (and confined) file path does not exist, rather than the same error used for a path-confinement rejection.

#### Scenario: File does not exist
- **WHEN** an authenticated caller requests a path that resolves within the named repository but no file exists there
- **THEN** the system responds with a not-found signal, distinguishable from a confinement-violation rejection

### Requirement: A run id is assigned before the transfer streams
The system SHALL assign the transfer a unique run identifier, delivered to the caller before the file content begins streaming, the same way `dotnet-command-execution` delivers a run id before output streaming begins.

#### Scenario: Run id precedes content
- **WHEN** an authenticated caller starts a valid transfer
- **THEN** the system delivers a run identifier before any file content is streamed

### Requirement: Caller can cancel an in-flight transfer
The system SHALL accept an authenticated cancellation request identifying a transfer by its run id, using the same cancellation endpoint used for `dotnet` and `git` runs, and SHALL stop streaming that transfer if it is still in flight.

#### Scenario: Cancelling a running transfer
- **WHEN** an authenticated caller cancels the run id of a transfer that is still in flight
- **THEN** the system stops streaming the file and the transfer's recorded outcome reflects cancellation

### Requirement: A transfer is always bounded, even if the connection dies silently
The system SHALL bound every transfer by the same configured maximum execution timeout `dotnet-command-execution` uses, independent of whether the caller's connection is detected as broken - since a connection that dies without a clean close may never be observed by the server (nothing left to write that could fail), a transfer must not be able to remain in flight indefinitely on that basis alone.

#### Scenario: A transfer whose connection never completes still reaches a terminal state
- **WHEN** a transfer's client connection dies without a clean close, and the server never attempts another write on it
- **THEN** the transfer is still killed once the configured maximum execution timeout elapses, and its recorded outcome reflects a timeout

### Requirement: Transfers do not contend for the per-repository command lock
The system SHALL NOT require a repository's per-repository command lock (used by `dotnet-command-execution` and `git-command-execution`) to be free before starting a transfer against that repository, and SHALL NOT hold that lock during a transfer — a transfer is a read of already-produced files, not a command execution.

#### Scenario: Transfer proceeds while a command is in flight
- **WHEN** a `dotnet build` run is in flight against `repository-a`
- **THEN** an authenticated caller can still start and complete a file transfer against `repository-a` without being rejected as a conflict

### Requirement: Transfers are recorded in history like command runs
The system SHALL record every transfer in the same durable history store used by `dotnet-command-execution` and `git-command-execution`, at the same lifecycle points (start, terminal state), per `run-history` — distinguished from a command run by its recorded kind, and carrying transfer-specific metadata (repository, requested file path, file size in bytes, start and completion timestamps, and outcome) in place of arguments and stdout/stderr.

#### Scenario: A completed transfer appears in history with its metadata
- **WHEN** a transfer completes successfully
- **THEN** its history record includes the repository, the requested file path, the transferred file's size in bytes, its start and completion timestamps, and a completed outcome

#### Scenario: A failed transfer appears in history
- **WHEN** a transfer is rejected for a confinement violation or a missing file
- **THEN** its history record (if a run id was assigned) reflects that outcome, distinguishable from a completed transfer

#### Scenario: A transfer that fails because the file couldn't be opened is distinguishable from a cancellation
- **WHEN** the resolved file exists but can't actually be opened (locked by another process, or a permission problem)
- **THEN** its history record reflects a failed outcome, distinct from a cancelled or timed-out one, and no file content is streamed

### Requirement: A transfer's resource usage is tracked using host-level sampling
The system SHALL record a resource-usage time series for a transfer's duration, per `run-history`, sourced from the same host-level CPU/RAM samples `host-resource-monitoring` already produces — since a transfer spawns no child process of its own, there is no process tree to aggregate the way there is for a `dotnet` or `git` run.

#### Scenario: A transfer's resource series is retrievable
- **WHEN** an operator requests the resource usage series for a completed transfer
- **THEN** the system returns host-level CPU/RAM samples spanning the transfer's duration, in the same series shape used for command runs

### Requirement: No directory listing
The system SHALL NOT provide a way to enumerate a repository's files or directories through this capability — the caller must already know the file path it wants. Listing/browsing a repository's contents is out of scope for v1.

#### Scenario: Requesting a directory instead of a file
- **WHEN** an authenticated caller requests a path that resolves to a directory rather than a file
- **THEN** the system rejects the request rather than returning a directory listing
