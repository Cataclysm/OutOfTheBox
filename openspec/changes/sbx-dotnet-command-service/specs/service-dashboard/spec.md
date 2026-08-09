## Purpose

Gives a human operator a pleasant, at-a-glance view of what the service is doing right now and what it has done historically, without needing to query the API by hand — sourced from `run-history` and, for repository inventory, `repository-management`.

## ADDED Requirements

### Requirement: Dashboard identifies the running software, and its version on the Status view
The system SHALL display the service's name on every dashboard view (e.g. in a persistent header or footer). The running version, sourced from the same build version `/version` reports, SHALL be shown once, on the Status view, rather than repeated on every view.

#### Scenario: Service name is visible from any view
- **WHEN** an operator is on any dashboard view
- **THEN** the service's name is visible without navigating elsewhere

#### Scenario: Running version is visible on the Status view
- **WHEN** an operator is on the Status view
- **THEN** the running version is visible

### Requirement: Dashboard shows current in-flight runs of every kind
The system SHALL provide a human-readable web page listing every currently in-flight run — `dotnet` commands, `git` commands, file transfers, and repository clones alike — with its repository, run id, start time, elapsed time, and kind-appropriate detail (arguments for a command run, requested file path for a transfer, source URL for a clone).

#### Scenario: Viewing an active command run
- **WHEN** a `dotnet` or `git` run is in flight and an operator opens the dashboard
- **THEN** the dashboard displays that run in the current-status view with its kind, repository, arguments, and elapsed time

#### Scenario: Viewing an active transfer
- **WHEN** a file transfer is in flight and an operator opens the dashboard
- **THEN** the dashboard displays that transfer in the current-status view with its kind, repository, requested file path, and elapsed time

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

### Requirement: Dashboard provides browsable history with kind-appropriate detail
The system SHALL provide a human-readable view of past runs of every kind (most recent first), showing each run's kind in the summary list, and let the operator open a specific run to view its full detail: for a `dotnet`/`git` run, its command, repository, timestamps, outcome, and complete stdout/stderr; for a file transfer, its repository, requested file path, file size, timestamps, and outcome.

#### Scenario: Browsing history
- **WHEN** an operator opens the history view
- **THEN** they see past runs of every kind listed most-recent-first, each showing its kind and enough summary detail to identify it

#### Scenario: Viewing a past command run's output
- **WHEN** an operator selects a specific past `dotnet` or `git` run
- **THEN** the dashboard displays that run's full stdout/stderr as it was persisted, including a truncation indicator if the record was truncated

#### Scenario: Viewing a past transfer's metadata
- **WHEN** an operator selects a specific past file transfer
- **THEN** the dashboard displays that transfer's repository, requested file path, file size, start/completion timestamps, and outcome — not a stdout/stderr panel

### Requirement: History view provides filter and search controls
The system SHALL let the operator filter the history view by run kind, outcome, and repository (per `run-history`'s filtering capability), and SHALL provide a free-text search box that filters the visible list as the operator types, combinable with the active filters.

#### Scenario: Filtering history by kind
- **WHEN** an operator selects the `git` kind filter in the history view
- **THEN** only `git` runs remain visible in the list

#### Scenario: Searching history
- **WHEN** an operator types text into the history search box
- **THEN** the visible list narrows to runs whose repository, arguments, or file path match that text, combined with any active kind/outcome/repository filters

#### Scenario: Clearing filters and search
- **WHEN** an operator clears all active filters and the search box
- **THEN** the history view again shows every past run, most-recent-first

### Requirement: Dashboard access requires the same credential as the API
The system SHALL require the same bearer/API-key credential used by the service's API endpoints (`dotnet` and `git` command execution, file transfer) before granting access to the dashboard, rather than introducing a separate unauthenticated human-facing surface.

#### Scenario: Unauthenticated dashboard access
- **WHEN** a browser requests the dashboard without presenting a valid credential
- **THEN** the system does not display run status or history content

### Requirement: Dashboard renders in dark mode only
The system SHALL render the dashboard in a dark color scheme and SHALL NOT require or expose a light-mode alternative.

#### Scenario: Opening the dashboard
- **WHEN** an operator opens the dashboard in any browser, regardless of the browser's or OS's own light/dark preference
- **THEN** the dashboard renders in dark mode

### Requirement: Dashboard organizes distinct concerns into distinct views and subpages
The system SHALL organize current-run status, host/process resource monitoring (per `host-resource-monitoring`), run history, and repository inventory (per `repository-management`) into distinct top-level views (e.g. separate pages or tabs) — **Status**, **History**, and **Repositories** — rather than presenting all of them on a single page at once, and SHALL further break out per-item detail (a specific run, a specific repository) into its own subpage reached by selecting that item from its list, rather than expanding inline detail into an already-dense list view.

#### Scenario: Navigating the dashboard
- **WHEN** an operator wants to check host resource usage versus browsing past run history versus reviewing the repository inventory
- **THEN** these are reachable as distinct top-level views, not competing for space on one combined page

#### Scenario: Run detail is its own subpage
- **WHEN** an operator selects a specific past run from the History list
- **THEN** its full detail opens as its own page, not as inline expansion within the list

#### Scenario: Repository detail is its own subpage
- **WHEN** an operator selects a specific repository from the Repositories list
- **THEN** its full detail (stats, git status, and its own run history) opens as its own page, not as inline expansion within the list

### Requirement: Repositories view lists every repository with live stats
The system SHALL provide a **Repositories** view listing every repository (per `repository-management`) with its name, total size, git status summary, and active/idle indicator, updating live as those stats and active/idle state change, without a manual page reload.

#### Scenario: Viewing the repository list
- **WHEN** an operator opens the Repositories view
- **THEN** they see every repository with its size, git status, and active/idle indicator

#### Scenario: Active indicator updates live
- **WHEN** a command starts or finishes against a repository while an operator has the Repositories view open
- **THEN** that repository's active/idle indicator updates without a page reload

### Requirement: Repositories view provides filter and search controls
The system SHALL let the operator filter the Repositories view by active/idle state and git status (clean/dirty, no-git), and provide a free-text search box matching repository name, narrowing the visible list as the operator types.

#### Scenario: Filtering to active repositories
- **WHEN** an operator filters the Repositories view to active repositories only
- **THEN** only repositories currently holding the per-repository command lock remain visible

#### Scenario: Searching repositories by name
- **WHEN** an operator types text into the Repositories search box
- **THEN** the visible list narrows to repositories whose name matches that text

### Requirement: Repositories view provides clone, delete, and per-repository git action controls, all as icons
The system SHALL provide a control to clone a new repository (opening a popup dialog prompting for a source URL, a name, and — once the URL is entered — a branch dropdown per `repository-management`) and, per listed repository, controls for delete, pull, push, force-push, fetch, and clean — all invoking `repository-management`'s corresponding actions. Every one of these list/row controls SHALL be rendered as an icon (not a text label), using recognizable iconography consistent with common practice (e.g. a trash can for delete), styled to match the dashboard's existing dark, minimal aesthetic. Destructive or irreversible actions (delete, force-push, clean) SHALL require an explicit confirmation step, presented as a popup dialog, before proceeding — not by the control itself changing state/label in place.

#### Scenario: Cloning from the dashboard
- **WHEN** an operator opens the clone dialog, enters a valid URL and an available name, and optionally selects a branch
- **THEN** the clone starts, appears as an in-flight run in the Status view, and the new repository appears in the Repositories list once complete

#### Scenario: Clone dialog is a popup, not an inline reveal
- **WHEN** an operator opens the clone control
- **THEN** the clone form appears as a popup dialog overlaying the page, rather than an inline section unhidden below the toolbar

#### Scenario: Destructive actions require a confirmation popup
- **WHEN** an operator selects delete, force-push, or clean for a repository
- **THEN** the system shows a confirmation popup dialog and only proceeds if the operator explicitly confirms in that dialog, rather than the control itself morphing into a confirm state on first click

#### Scenario: List and row controls are icons
- **WHEN** an operator views the Repositories list
- **THEN** every action (clone, delete, pull, push, force-push, fetch, clean) is represented by an icon rather than a text-labeled button

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

#### Scenario: Viewing a live transfer's resource graph
- **WHEN** a file transfer has been in flight for several minutes
- **THEN** its CPU/RAM graph shows the same host-level samples recorded for it (per `file-transfer`), in the same graph presentation used for command runs

### Requirement: A completed run's full resource graph is viewable in history
The system SHALL, on a run's detail view in the History view, render that run's persisted resource usage series (per `run-history`) as a graph covering its entire duration, not limited to the 10-minute live window — for every run kind, including file transfers.

#### Scenario: Viewing a past run's full graph
- **WHEN** an operator opens a completed run's detail view
- **THEN** they see a CPU/RAM graph spanning that run's entire recorded duration, from start to its terminal state

#### Scenario: Viewing a past transfer's full graph
- **WHEN** an operator opens a completed file transfer's detail view
- **THEN** they see the same kind of CPU/RAM graph spanning the transfer's duration
