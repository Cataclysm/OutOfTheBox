# host-resource-monitoring Specification

## Purpose
Gives the operator visibility into host and process resource usage while `dotnet`/`git` commands run, and a way to terminate a specific hung process (e.g. a `testhost.exe` that won't exit) without necessarily tearing down the whole run. Also backs the host-level resource series recorded for file transfers (per `file-transfer`), which spawn no process tree of their own.
## Requirements
### Requirement: Host CPU usage is sampled, total and per-core
The system SHALL periodically sample total host CPU utilization and per-core/per-thread CPU utilization, refreshed at least every few seconds.

#### Scenario: Viewing CPU usage
- **WHEN** an operator views the resource monitoring data
- **THEN** it includes a total CPU utilization figure and a utilization figure for each logical core, no more than a few seconds stale

### Requirement: Host and service RAM usage are sampled
The system SHALL periodically sample total host RAM usage (used vs. total) and the resident memory usage of the service's own process, refreshed at least every few seconds.

#### Scenario: Viewing RAM usage
- **WHEN** an operator views the resource monitoring data
- **THEN** it includes total host RAM used/available and the service process's own memory usage, no more than a few seconds stale

### Requirement: Host network usage is sampled
The system SHALL periodically sample host-wide network throughput (bytes sent and received per second, summed across every network interface), refreshed at least every few seconds, on the same cadence as CPU/RAM sampling.

#### Scenario: Viewing network usage
- **WHEN** an operator views the resource monitoring data
- **THEN** it includes a network bytes-sent-per-second figure and a network bytes-received-per-second figure, combined across every network interface, no more than a few seconds stale

### Requirement: Host disk I/O usage is sampled, all drives combined
The system SHALL periodically sample host-wide disk read and write throughput (bytes per second, summed across every physical drive), refreshed at least every few seconds, on the same cadence as CPU/RAM/network sampling.

#### Scenario: Viewing disk I/O usage
- **WHEN** an operator views the resource monitoring data
- **THEN** it includes a disk read throughput figure and a disk write throughput figure, combined across every physical drive, no more than a few seconds stale

### Requirement: Spawned processes are listed with resource usage
The system SHALL enumerate the process tree rooted at each process the service itself launched (a `dotnet.exe` or `git.exe` invocation and all of its descendants, such as `testhost.exe` or compiler worker processes) and report each one's name, process id, CPU usage, and RAM usage, refreshed at least every few seconds.

#### Scenario: Viewing spawned processes
- **WHEN** a run is in flight and has spawned child processes
- **THEN** the operator sees each of those processes listed with its name, process id, CPU usage, and RAM usage

#### Scenario: No spawned processes
- **WHEN** no run is in flight
- **THEN** the spawned-process list is empty rather than showing stale entries from a finished run

### Requirement: A run's aggregate CPU and RAM usage is tracked
The system SHALL compute, at the same cadence as process sampling, each in-flight run's aggregate CPU usage and RAM usage as the sum across every process in that run's spawned process tree, in addition to the per-process figures. Since neither network nor disk I/O can be isolated to one process tree on Windows (there is no per-process network-only counter, and disk I/O is sampled as a whole-machine figure), the system SHALL additionally tag each in-flight run with that same tick's host-level network and disk I/O figures - not an isolated per-run measurement, but the best available signal given the platform constraint.

#### Scenario: Viewing a run's aggregate usage
- **WHEN** a run is in flight with one or more spawned processes
- **THEN** the operator can see that run's combined CPU and RAM usage as a single figure, not only the individual per-process breakdown

#### Scenario: A run's network and disk figures reflect the host, not an isolated measurement
- **WHEN** a run is in flight
- **THEN** the network and disk I/O figures recorded against it are that tick's host-wide figures, the same as recorded against the host series at that moment - not a per-process or per-run-isolated measurement

### Requirement: Operator can terminate a spawned process and its descendants
The system SHALL let an operator request termination of any process appearing in the spawned-process list, and SHALL terminate that process together with its own descendants (not just the single process), so a hung leaf process (e.g. `testhost.exe`) can be cleared without leaving orphaned children behind.

#### Scenario: Killing a hung child process
- **WHEN** an operator requests termination of a listed process (for example a hung `testhost.exe`)
- **THEN** the system terminates that process and any processes descended from it, and it no longer appears in the spawned-process list on the next refresh

### Requirement: Termination is scoped to the service's own spawned processes
The system SHALL only permit terminating a process that is the service's own launched process or a descendant of one, and SHALL reject a termination request for any other process id, regardless of what id is supplied.

#### Scenario: Rejecting termination of an unrelated process
- **WHEN** a termination request targets a process id that is not part of any process tree the service itself spawned
- **THEN** the system rejects the request and does not terminate that process

