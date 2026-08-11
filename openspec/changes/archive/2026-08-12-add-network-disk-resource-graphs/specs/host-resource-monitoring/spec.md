## MODIFIED Requirements

### Requirement: A run's aggregate CPU and RAM usage is tracked

The system SHALL compute, at the same cadence as process sampling, each in-flight run's aggregate CPU usage and RAM usage as the sum across every process in that run's spawned process tree, in addition to the per-process figures. Since neither network nor disk I/O can be isolated to one process tree on Windows (there is no per-process network-only counter, and disk I/O is sampled as a whole-machine figure), the system SHALL additionally tag each in-flight run with that same tick's host-level network and disk I/O figures - not an isolated per-run measurement, but the best available signal given the platform constraint.

#### Scenario: Viewing a run's aggregate usage
- **WHEN** a run is in flight with one or more spawned processes
- **THEN** the operator can see that run's combined CPU and RAM usage as a single figure, not only the individual per-process breakdown

#### Scenario: A run's network and disk figures reflect the host, not an isolated measurement
- **WHEN** a run is in flight
- **THEN** the network and disk I/O figures recorded against it are that tick's host-wide figures, the same as recorded against the host series at that moment - not a per-process or per-run-isolated measurement

## ADDED Requirements

### Requirement: Host network usage is sampled

The system SHALL periodically sample host-wide network throughput (bytes sent and received per second, summed across every network interface), refreshed at least every few seconds, on the same cadence as CPU/RAM sampling. (This documents behavior already shipped - network sampling existed in the implementation before this requirement was written down; folded in here alongside disk I/O rather than left undocumented any longer.)

#### Scenario: Viewing network usage
- **WHEN** an operator views the resource monitoring data
- **THEN** it includes a network bytes-sent-per-second figure and a network bytes-received-per-second figure, combined across every network interface, no more than a few seconds stale

### Requirement: Host disk I/O usage is sampled, all drives combined

The system SHALL periodically sample host-wide disk read and write throughput (bytes per second, summed across every physical drive), refreshed at least every few seconds, on the same cadence as CPU/RAM/network sampling.

#### Scenario: Viewing disk I/O usage
- **WHEN** an operator views the resource monitoring data
- **THEN** it includes a disk read throughput figure and a disk write throughput figure, combined across every physical drive, no more than a few seconds stale
