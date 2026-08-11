## MODIFIED Requirements

### Requirement: A run's resource usage over its lifetime is persisted

The system SHALL persist a time series of each run's aggregate CPU and RAM usage (per `host-resource-monitoring`) from the moment it starts until it reaches a terminal state, at the same sampling cadence used for live monitoring, and SHALL retain the complete series for a completed run regardless of how long the run lasted. Each sample SHALL additionally carry that tick's host-level network and disk I/O figures (per `host-resource-monitoring`'s own requirement that these are tagged onto every run, not isolated per-run measurements) - absent (not zero) for any sample persisted before this pair of figures existed.

#### Scenario: Full-duration series is retrievable after completion
- **WHEN** an operator requests the resource usage series for a run that has finished
- **THEN** the system returns samples spanning the run's entire duration, from start to its terminal state, not only a recent window

#### Scenario: Series exists for an in-flight run
- **WHEN** an operator requests the resource usage series for a run that is still in flight
- **THEN** the system returns the samples collected so far for that run

#### Scenario: A sample predating network/disk tracking has no value for those figures
- **WHEN** an operator requests the resource usage series for a run with samples recorded before network/disk figures were tracked
- **THEN** those samples report no network/disk figures rather than a misleading zero reading
