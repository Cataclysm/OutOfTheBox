## Purpose

Gives a human operator a pleasant, at-a-glance view of what the service is doing right now and what it has done historically, without needing to query the API by hand — sourced entirely from `run-history`.

## ADDED Requirements

### Requirement: Dashboard shows current in-flight runs
The system SHALL provide a human-readable web page listing every currently in-flight run (repo, arguments, run id, start time, elapsed time).

#### Scenario: Viewing an active run
- **WHEN** a run is in flight and an operator opens the dashboard
- **THEN** the dashboard displays that run in the current-status view with its repo, arguments, and elapsed time

#### Scenario: No runs in flight
- **WHEN** no run is currently in flight
- **THEN** the dashboard's current-status view indicates the service is idle rather than showing a stale or empty ambiguous state

### Requirement: Dashboard updates live without manual refresh
The system SHALL reflect changes in run status (new run started, output produced, run completed) on the dashboard automatically, without the operator reloading the page.

#### Scenario: New run appears automatically
- **WHEN** a new run starts while an operator has the dashboard open
- **THEN** that run appears in the current-status view without the operator reloading the page

#### Scenario: Completion moves a run from status to history live
- **WHEN** an in-flight run reaches a terminal state while an operator has the dashboard open
- **THEN** the dashboard reflects the run's terminal outcome in place (or moves it into the history view) without a manual reload

### Requirement: Dashboard provides browsable history with output detail
The system SHALL provide a human-readable view of past runs (most recent first) and let the operator open a specific run to view its full command, repo, timestamps, outcome, and complete stdout/stderr.

#### Scenario: Browsing history
- **WHEN** an operator opens the history view
- **THEN** they see past runs listed most-recent-first with enough summary detail to identify each one

#### Scenario: Viewing a past run's output
- **WHEN** an operator selects a specific past run
- **THEN** the dashboard displays that run's full stdout/stderr as it was persisted, including a truncation indicator if the record was truncated

### Requirement: Dashboard access requires the same credential as the API
The system SHALL require the same bearer/API-key credential used by the command-execution API before granting access to the dashboard, rather than introducing a separate unauthenticated human-facing surface.

#### Scenario: Unauthenticated dashboard access
- **WHEN** a browser requests the dashboard without presenting a valid credential
- **THEN** the system does not display run status or history content

### Requirement: Dashboard renders in dark mode only
The system SHALL render the dashboard in a dark color scheme and SHALL NOT require or expose a light-mode alternative.

#### Scenario: Opening the dashboard
- **WHEN** an operator opens the dashboard in any browser, regardless of the browser's or OS's own light/dark preference
- **THEN** the dashboard renders in dark mode

### Requirement: Dashboard separates status, resource monitoring, and history into distinct views
The system SHALL organize current-run status, host/process resource monitoring (per `host-resource-monitoring`), and run history into distinct views (e.g. separate pages or tabs) rather than presenting all of them on a single page at once.

#### Scenario: Navigating the dashboard
- **WHEN** an operator wants to check host resource usage versus browsing past run history
- **THEN** these are reachable as distinct views, not competing for space on one combined page

### Requirement: Current-status view includes host and process resource monitoring
The system SHALL surface `host-resource-monitoring` data (host CPU/RAM, service RAM, spawned-process list with kill action) alongside in-flight run status, grouped so that a run's spawned processes are shown associated with that run rather than in an undifferentiated flat list.

#### Scenario: Spawned processes shown under their run
- **WHEN** a run is in flight and has spawned child processes
- **THEN** those processes are presented in association with that run, not merged indistinguishably with processes spawned by other runs

### Requirement: Resource usage is presented as graphs
The system SHALL present host CPU/RAM and per-run CPU/RAM as graphs (not only current numeric values), covering a recent rolling window of at least 10 minutes on the live status view, updating live at the resource-sampling cadence.

#### Scenario: Viewing live host resource graphs
- **WHEN** an operator views the Status view
- **THEN** host CPU and RAM are shown as graphs covering at least the last 10 minutes, extending forward as new samples arrive

#### Scenario: Viewing a live run's resource graph
- **WHEN** a run has been in flight for several minutes
- **THEN** its CPU/RAM graph shows that run's own recent usage over time, not just its current instantaneous value

### Requirement: A completed run's full resource graph is viewable in history
The system SHALL, on a run's detail view in the History view, render that run's persisted resource usage series (per `run-history`) as a graph covering its entire duration, not limited to the 10-minute live window.

#### Scenario: Viewing a past run's full graph
- **WHEN** an operator opens a completed run's detail view
- **THEN** they see a CPU/RAM graph spanning that run's entire recorded duration, from start to its terminal state
