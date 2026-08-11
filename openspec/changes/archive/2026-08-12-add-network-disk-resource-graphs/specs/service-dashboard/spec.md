## MODIFIED Requirements

### Requirement: Resource usage is presented as graphs

The system SHALL present host CPU/RAM/network/disk I/O and per-run CPU/RAM as graphs (not only current numeric values), covering a recent rolling window on the live status view, updating live at the resource-sampling cadence. The host CPU graph alone SHALL cover at least the last 20 minutes and SHALL be laid out alone, scaled to the full available width; every other live graph (including this same CPU figure shown for a specific run) SHALL cover at least the last 10 minutes. A graph with only one line SHALL NOT show a legend (redundant with its own heading); a graph with more than one line (network's Sent/Received, disk I/O's Read/Write) SHALL show one, so its lines can be told apart.

#### Scenario: Viewing live host resource graphs
- **WHEN** an operator views the Status page
- **THEN** host CPU is shown alone, scaled to the full row width, covering at least the last 20 minutes and extending forward as new samples arrive, with no legend
- **AND** host CPU-per-core, RAM, network, and disk I/O are each shown covering at least the last 10 minutes, network and disk I/O each with a legend distinguishing their two lines, CPU-per-core and RAM without one

#### Scenario: Viewing a live run's resource graph
- **WHEN** an operator expands a specific run's card on the Status page
- **THEN** its CPU/RAM graph shows that run's own recent usage over time, not just its current instantaneous value, with no legend on either

#### Scenario: Viewing a live transfer's resource graph
- **WHEN** an operator expands a specific file transfer's card on the Status page
- **THEN** its CPU/RAM graph shows the same host-level samples recorded for it (per `file-transfer`), in the same graph presentation used for command runs

### Requirement: A run's full resource graph is viewable in history, live while still in flight

The system SHALL, on a run's detail view in the History view, render that run's persisted resource usage series (per `run-history`) as graphs covering its entire duration - for every run kind, including file transfers. This SHALL include CPU, RAM, network, and disk I/O, laid out as two rows (CPU and RAM together, then network and disk I/O together). CPU and RAM SHALL NOT show a legend; network and disk I/O SHALL each show one, distinguishing their two lines (Sent/Received, Read/Write). If the run is still in flight when its detail page is viewed, these graphs SHALL continue extending live as new samples are recorded, with no fixed rolling window - unlike the Status page's live graphs, the dataset SHALL keep growing for as long as the run stays open rather than sliding a fixed-width window. A run viewed before it has any recorded samples yet SHALL show an empty-state message that transitions to the graphs themselves the moment its first sample is recorded, without requiring a manual reload.

#### Scenario: Viewing a past run's full graph
- **WHEN** an operator opens a completed run's detail page
- **THEN** they see CPU, RAM, network, and disk I/O graphs spanning that run's entire recorded duration, from start to its terminal state, arranged as two rows - CPU and RAM first, network and disk I/O below

#### Scenario: Viewing a past transfer's full graph
- **WHEN** an operator opens a completed file transfer's detail page
- **THEN** they see the same kind of graphs (CPU, RAM, network, disk I/O) spanning the transfer's duration

#### Scenario: An in-flight run's graph extends live without a reload
- **WHEN** an operator opens the detail page of a run that is still in flight, and a new resource sample is recorded for it
- **THEN** the graphs extend to include that new sample without the operator reloading the page, and without discarding any earlier point to make room for it

#### Scenario: A run viewed before its first sample transitions out of the empty state
- **WHEN** an operator opens the detail page of a run that has no recorded samples yet, and its first sample is then recorded
- **THEN** the page replaces its empty-state message with the graphs, seeded with that first sample, without the operator reloading the page
