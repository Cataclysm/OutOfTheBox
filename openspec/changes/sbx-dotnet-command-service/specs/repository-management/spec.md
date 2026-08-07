## Purpose

Lets the human operator — not the sbx sandbox caller — manage the inventory of repositories the service operates on directly from the dashboard: see what's there and its current state at a glance, clone a new one in, and remove one that's no longer needed. Unlike `dotnet-command-execution`, `git-command-execution`, and `artifact-transfer`, this capability has no bearer-token REST surface: it's exposed only as authenticated in-process actions inside the Blazor dashboard, the same way process-kill (per `host-resource-monitoring`) is — the sbx caller has no way to clone or delete a repository.

A "repository" for the purposes of this capability is a top-level directory directly under the configured root.

## ADDED Requirements

### Requirement: Repositories are listed with identifying stats
The system SHALL enumerate every top-level directory under the configured root as a repository and report, for each: its name, total on-disk size, git status summary (current branch, clean/dirty, ahead/behind its upstream if one is configured — or an indication that it isn't a git repository at all), and whether it is currently active (holds the per-repo command lock per `dotnet-command-execution`/`git-command-execution`).

#### Scenario: Listing repositories
- **WHEN** an operator views the repository list
- **THEN** each repository is shown with its name, total size, git status summary, and active/idle state

#### Scenario: A non-git directory is listed without a git status
- **WHEN** a top-level directory under the root is not itself a git repository
- **THEN** the system lists it with its name and size, and indicates it has no git status rather than failing or omitting it

### Requirement: Repository stats update without manual refresh
The system SHALL recompute a repository's git status after any run (of any kind) against it reaches a terminal state, and SHALL periodically recompute both git status and total size on a background cadence independent of the command-resource sampler, so the list reflects reality without the operator needing to reload the page. The system SHALL reflect a repository's active/idle transitions live, sourced from the same run-started/run-terminal signals `service-dashboard` already subscribes to.

#### Scenario: Active state updates live
- **WHEN** a `dotnet`/`git` run starts or ends against a repository while an operator has the repository list open
- **THEN** that repository's active/idle indicator updates without the operator reloading the page

#### Scenario: Git status refreshes after a run completes
- **WHEN** a `git pull` run against a repository completes
- **THEN** that repository's git status summary (branch, dirty/clean, ahead/behind) is recomputed and reflects the post-pull state without requiring a manual refresh

### Requirement: A new repository can be cloned
The system SHALL let an operator clone a new repository by supplying a source URL and a name, resolve the name under the configured root (rejecting a name that would escape the root or that already exists), and run `git clone <url>` targeting that resolved, not-yet-existing directory — streaming its output the same way `git-command-execution` streams output, assigning it a run id, and recording it in history (per `run-history`) with a distinct kind.

#### Scenario: Successful clone
- **WHEN** an operator clones a repository by URL under a name that doesn't already exist
- **THEN** the system runs `git clone`, the new repository appears in the repository list once complete, and a history record exists for the clone with its source URL and outcome

#### Scenario: Clone target name already exists
- **WHEN** an operator attempts to clone using a name that already names an existing repository
- **THEN** the system rejects the request without invoking `git.exe` or modifying the existing repository

#### Scenario: Clone source is not restricted
- **WHEN** an operator supplies any git-reachable URL as the clone source
- **THEN** the system attempts the clone without validating the URL against an allowlist — the same unrestricted trust model as `git-command-execution`

### Requirement: Cloning a repository acquires its per-repo lock
The system SHALL acquire the same per-repo command lock used by `dotnet-command-execution`/`git-command-execution`, keyed by the clone's target path, for the duration of the clone — preventing a concurrent duplicate clone into the same target name, and preventing any command from being accepted against that target until the clone finishes.

#### Scenario: Duplicate concurrent clone is rejected
- **WHEN** a clone into a given name is already in flight and a second clone into that same name is requested before the first finishes
- **THEN** the system rejects the second request with a conflict identifying the in-flight clone's run id

#### Scenario: Commands against a mid-clone target are rejected
- **WHEN** a clone is in flight and, before it finishes, a `dotnet` or `git` command targets the same (partially cloned) repository
- **THEN** the system rejects that command the same way it would reject one against any other busy repository

### Requirement: An existing repository can be deleted
The system SHALL let an operator delete an existing repository, permanently and recursively removing its directory from disk, resolved and confined under the configured root the same way every other repo-targeting operation is. Deletion SHALL be recorded in history (per `run-history`) with a distinct kind.

#### Scenario: Successful deletion
- **WHEN** an operator deletes an existing, idle repository
- **THEN** the system removes its directory recursively, it no longer appears in the repository list, and a history record exists for the deletion with its outcome

#### Scenario: Deleting a nonexistent repository
- **WHEN** an operator attempts to delete a name that does not resolve to an existing repository under the root
- **THEN** the system rejects the request with a not-found signal rather than silently succeeding

### Requirement: Deletion is rejected while the repository is active
The system SHALL require a repository's per-repo command lock to be free before deleting it, and SHALL reject a deletion request for a repository that currently has an in-flight `dotnet`/`git` run (or clone) rather than deleting out from under it.

#### Scenario: Deletion of a busy repository is rejected
- **WHEN** a `dotnet build` is in flight against a repository and an operator attempts to delete it
- **THEN** the system rejects the deletion with a conflict identifying the in-flight run, and does not remove any files

### Requirement: An in-flight clone can be cancelled from the dashboard, not the REST cancel endpoint
The system SHALL let an operator cancel an in-flight repository clone from the dashboard, and SHALL NOT accept a repository-management run's id on the bearer-token `POST /run/{runId}/cancel` endpoint used by `dotnet-command-execution`/`git-command-execution`/`artifact-transfer` — cancelling a clone is an in-process dashboard action, consistent with clone/delete having no REST surface at all.

#### Scenario: Cancelling a clone from the dashboard
- **WHEN** an operator cancels an in-flight clone from the Repos or Status view
- **THEN** the system stops the clone, its history record reflects cancellation, and its lock is released

#### Scenario: The REST cancel endpoint does not affect repository-management runs
- **WHEN** a bearer-token caller sends a cancellation request naming a repository clone's run id to `POST /run/{runId}/cancel`
- **THEN** the system responds as if the run id were unknown, rather than cancelling the clone

### Requirement: Repository management is reachable only from the authenticated dashboard
The system SHALL NOT expose repository listing, cloning, or deletion as a bearer-token REST endpoint reachable by the sbx sandbox caller — these actions are available only as authenticated in-process operations within the Blazor dashboard, gated by the same dashboard authentication as everything else in `service-dashboard`.

#### Scenario: No REST endpoint exists for repository management
- **WHEN** a caller presents a valid bearer credential to the service's command/artifact API surface
- **THEN** no request against that API surface can list, clone, or delete a repository — those actions exist only inside the authenticated dashboard
