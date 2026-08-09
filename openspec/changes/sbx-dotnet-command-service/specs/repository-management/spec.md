## Purpose

Lets the human operator — not the sbx sandbox caller — manage the inventory of repositories the service operates on directly from the dashboard: see what's there and its current state at a glance, clone a new one in, and remove one that's no longer needed. Unlike `dotnet-command-execution`, `git-command-execution`, and `file-transfer`, this capability has no bearer-token REST surface: it's exposed only as authenticated in-process actions inside the Blazor dashboard, the same way process-kill (per `host-resource-monitoring`) is — the sbx caller has no way to clone or delete a repository.

A "repository" for the purposes of this capability is a top-level directory directly under the configured root.

## ADDED Requirements

### Requirement: Repositories are listed with identifying stats
The system SHALL enumerate every top-level directory under the configured root as a repository and report, for each: its name, total on-disk size, git status summary (current branch, clean/dirty, ahead/behind its upstream if one is configured, or a distinct "remote branch gone" indication if a tracking branch was configured but its remote-side branch no longer exists — or an indication that it isn't a git repository at all), and whether it is currently active (holds the per-repository command lock per `dotnet-command-execution`/`git-command-execution`). Its clone source URL (if known) and its configured remotes (name and URL) SHALL additionally be shown on its detail subpage (per `service-dashboard`'s "Repository detail is its own subpage").

#### Scenario: Listing repositories
- **WHEN** an operator views the repository list
- **THEN** each repository is shown with its name, total size, git status summary, and active/idle state

#### Scenario: A non-git directory is listed without a git status
- **WHEN** a top-level directory under the root is not itself a git repository
- **THEN** the system lists it with its name and size, and indicates it has no git status rather than failing or omitting it

#### Scenario: Repository detail shows clone source and remotes
- **WHEN** an operator opens a repository's detail subpage
- **THEN** the system shows the repository's clone source URL (if it was cloned by this service) and its full list of configured remotes (name and URL), in addition to its git status summary

#### Scenario: A remote-tracking branch whose remote branch was deleted is distinguished from no upstream
- **WHEN** a repository's current branch has a configured upstream but that upstream's remote-side branch no longer exists
- **THEN** the system's git status summary states the remote branch is gone, rather than reporting it the same way as a branch with no upstream configured at all

### Requirement: Repository stats update on two independent cadences
The system SHALL recompute a repository's git status (branch, dirty/clean, ahead/behind, gone-remote detection) after any run (of any kind) against it reaches a terminal state, and SHALL periodically recompute git status on a fast background cadence and total on-disk size on a separate, slower background cadence — both independent of the command-resource sampler — so the list reflects reality without the operator needing to reload the page. Size computation (a full recursive directory walk) is materially more expensive than git status (a handful of short-lived `git` invocations), so it SHALL be polled less frequently by default. The system SHALL reflect a repository's active/idle transitions live, sourced from the same run-started/run-terminal signals `service-dashboard` already subscribes to.

#### Scenario: Active state updates live
- **WHEN** a `dotnet`/`git` run starts or ends against a repository while an operator has the repository list open
- **THEN** that repository's active/idle indicator updates without the operator reloading the page

#### Scenario: Git status refreshes after a run completes
- **WHEN** a `git pull` run against a repository completes
- **THEN** that repository's git status summary (branch, dirty/clean, ahead/behind) is recomputed and reflects the post-pull state without requiring a manual refresh

#### Scenario: Git status is polled more frequently than size
- **WHEN** the service has been running for longer than one git-status interval but less than one size interval
- **THEN** a repository's git status has already been recomputed at least once on the background cadence while its total size has not yet been recomputed again since startup

#### Scenario: A single repository's stats failure does not stop other repositories from updating
- **WHEN** computing stats for one repository fails (e.g. a git invocation cannot start)
- **THEN** the system skips that repository for the current cycle and continues computing and publishing stats for every other repository, rather than the failure interrupting the background sampler entirely

### Requirement: A new repository can be cloned, optionally on a specific initial branch
The system SHALL let an operator clone a new repository by supplying a source URL, a name, and an optional initial branch, resolve the name under the configured root (rejecting a name that would escape the root or that already exists), and run `git clone <url>` (with `--branch <branch>` appended when one was supplied) targeting that resolved, not-yet-existing directory — streaming its output the same way `git-command-execution` streams output, assigning it a run id, and recording it in history (per `run-history`) with a distinct kind. This branch parameter SHALL be accepted both from the dashboard's clone dialog and from the bearer-token `POST /repositories/clone` endpoint.

#### Scenario: Successful clone
- **WHEN** an operator clones a repository by URL under a name that doesn't already exist
- **THEN** the system runs `git clone`, the new repository appears in the repository list once complete, and a history record exists for the clone with its source URL and outcome

#### Scenario: Clone target name already exists
- **WHEN** an operator attempts to clone using a name that already names an existing repository
- **THEN** the system rejects the request without invoking `git.exe` or modifying the existing repository

#### Scenario: Clone source is not restricted
- **WHEN** an operator supplies any git-reachable URL as the clone source
- **THEN** the system attempts the clone without validating the URL against an allowlist — the same unrestricted trust model as `git-command-execution`

#### Scenario: Cloning with an explicit initial branch
- **WHEN** an operator (or a `POST /repositories/clone` caller) supplies a branch name along with the URL and name
- **THEN** the system runs `git clone --branch <branch> <url>` and the cloned repository is checked out on that branch

#### Scenario: Cloning without a branch uses the remote's default
- **WHEN** no branch is supplied
- **THEN** the system runs a plain `git clone` and the cloned repository is checked out on the remote's default branch, as before this capability existed

### Requirement: The dashboard's clone dialog enumerates remote branches
The system SHALL, once an operator has entered a source URL in the clone dialog, enumerate that remote's branches (via a lightweight, dashboard-only lookup that does not itself clone anything) and present them as a dropdown selection for the initial-branch parameter, rather than requiring the operator to know and type an exact branch name.

#### Scenario: Branches populate after entering a URL
- **WHEN** an operator enters or changes the source URL in the clone dialog
- **THEN** the system queries that remote's branches and populates the branch dropdown with the results

#### Scenario: Branch enumeration failure does not block cloning
- **WHEN** the remote branch lookup fails (e.g. the URL is unreachable or invalid at the time of lookup)
- **THEN** the branch dropdown is left empty/unset rather than blocking the operator from attempting the clone with no explicit branch

### Requirement: Cloning a repository acquires its per-repository lock
The system SHALL acquire the same per-repository command lock used by `dotnet-command-execution`/`git-command-execution`, keyed by the clone's target path, for the duration of the clone — preventing a concurrent duplicate clone into the same target name, and preventing any command from being accepted against that target until the clone finishes.

#### Scenario: Duplicate concurrent clone is rejected
- **WHEN** a clone into a given name is already in flight and a second clone into that same name is requested before the first finishes
- **THEN** the system rejects the second request with a conflict identifying the in-flight clone's run id

#### Scenario: Commands against a mid-clone target are rejected
- **WHEN** a clone is in flight and, before it finishes, a `dotnet` or `git` command targets the same (partially cloned) repository
- **THEN** the system rejects that command the same way it would reject one against any other busy repository

### Requirement: An existing repository can be deleted
The system SHALL let an operator delete an existing repository, permanently and recursively removing its directory from disk, resolved and confined under the configured root the same way every other repository-targeting operation is. Deletion SHALL be recorded in history (per `run-history`) with a distinct kind.

#### Scenario: Successful deletion
- **WHEN** an operator deletes an existing, idle repository
- **THEN** the system removes its directory recursively, it no longer appears in the repository list, and a history record exists for the deletion with its outcome

#### Scenario: Deleting a nonexistent repository
- **WHEN** an operator attempts to delete a name that does not resolve to an existing repository under the root
- **THEN** the system rejects the request with a not-found signal rather than silently succeeding

### Requirement: Deletion is rejected while the repository is active
The system SHALL require a repository's per-repository command lock to be free before deleting it, and SHALL reject a deletion request for a repository that currently has an in-flight `dotnet`/`git` run (or clone) rather than deleting out from under it.

#### Scenario: Deletion of a busy repository is rejected
- **WHEN** a `dotnet build` is in flight against a repository and an operator attempts to delete it
- **THEN** the system rejects the deletion with a conflict identifying the in-flight run, and does not remove any files

### Requirement: Dashboard-only pull/push/force-push/fetch/clean actions per repository
The system SHALL let an operator trigger `git pull`, `git push`, `git push --force`, `git fetch`, and `git clean -xdf` against a repository directly from the Repositories list, each acquiring the repository's per-repository command lock (rejected the same way any other command would be if the repository is busy) for the duration of the operation. These actions SHALL NOT stream live output to the operator — the dashboard SHALL indicate only the outcome (success or failure) once the operation completes, by briefly recoloring the action's icon (green on success, red on failure) rather than opening a console/output view. Force-push and clean SHALL require explicit confirmation before proceeding, per the same confirmation mechanism as deletion.

#### Scenario: Pull updates the repository and its stats
- **WHEN** an operator triggers pull against an idle repository
- **THEN** the system runs `git pull` against it, and once it completes the repository's git status (branch, dirty/clean, ahead/behind) reflects the result

#### Scenario: Push and fetch acquire the repository lock
- **WHEN** an operator triggers push, force-push, or fetch against a repository that is currently busy with another run
- **THEN** the system rejects the action the same way it would reject any other command against a busy repository

#### Scenario: Force-push and clean require confirmation
- **WHEN** an operator selects the force-push or clean action
- **THEN** the system requires an explicit confirmation before running `git push --force` or `git clean -xdf`, rather than running it immediately on the first click

#### Scenario: Outcome is shown without a console view
- **WHEN** any of these actions completes, successfully or not
- **THEN** the dashboard reflects the outcome by recoloring that action's icon for a few seconds, without opening a streamed-output console the way `dotnet`/`git` runs do

### Requirement: Repository detail provides a branch-switch control
The system SHALL let an operator switch a repository's checked-out branch from its detail subpage, offering a dropdown enumerating both local branches and remote branches (`git branch -a`). Switching to a local branch SHALL run a plain checkout. Switching to a remote branch with no corresponding local branch SHALL automatically create a local tracking branch for it before checking it out, rather than requiring the operator to create the tracking branch themselves first.

#### Scenario: Switching to an existing local branch
- **WHEN** an operator selects a local branch from the branch-switch dropdown
- **THEN** the system checks out that branch directly

#### Scenario: Switching to a remote branch with no local counterpart
- **WHEN** an operator selects a remote branch that has no matching local branch
- **THEN** the system creates a local branch tracking that remote branch and checks it out, and the branch-switch dropdown subsequently lists it as a local branch

### Requirement: An in-flight clone can be cancelled from the dashboard, not the REST cancel endpoint
The system SHALL let an operator cancel an in-flight repository clone from the dashboard, and SHALL NOT accept a repository-management run's id on the bearer-token `POST /run/{runId}/cancel` endpoint used by `dotnet-command-execution`/`git-command-execution`/`file-transfer` — cancelling a clone is an in-process dashboard action, consistent with clone/delete having no REST surface at all.

#### Scenario: Cancelling a clone from the dashboard
- **WHEN** an operator cancels an in-flight clone from the Repositories or Status view
- **THEN** the system stops the clone, its history record reflects cancellation, and its lock is released

#### Scenario: The REST cancel endpoint does not affect repository-management runs
- **WHEN** a bearer-token caller sends a cancellation request naming a repository clone's run id to `POST /run/{runId}/cancel`
- **THEN** the system responds as if the run id were unknown, rather than cancelling the clone

### Requirement: Repository detail shows a branch-aware commit graph
The system SHALL show, on a repository's detail subpage, its commit history as a graph — a lane/connector visualization reflecting branch and merge topology, not a flat chronological list — with each commit showing its short hash, author, date, subject, and the names of any branches or tags pointing at it. The graph SHALL cover commits reachable from any branch (not just the currently checked-out one), paginated rather than loading unbounded history at once.

#### Scenario: Viewing the commit graph
- **WHEN** an operator opens a repository's detail subpage
- **THEN** they see a commit graph with lane/connector lines reflecting the repository's actual branch and merge structure, most-recent-first

#### Scenario: Commits carrying branch or tag names are labeled
- **WHEN** a commit is the tip of a branch or has a tag pointing at it
- **THEN** the graph shows that branch/tag name directly on the commit, distinguishing a local branch, a remote-tracking branch, and a tag from one another

#### Scenario: Loading more history
- **WHEN** an operator has viewed the initially-loaded page of commits and wants to see older ones
- **THEN** the system loads the next page of commit history rather than having loaded the entire history up front

### Requirement: A commit can be checked out as a detached HEAD
The system SHALL let an operator check out any commit shown in the graph, resulting in a detached HEAD at that commit, requiring explicit confirmation before proceeding given it changes the repository's checked-out state. The system SHALL distinguish a detached HEAD from a normal branch checkout wherever git status is displayed (repository list and detail), rather than showing the literal ref name `HEAD` as if it were a branch.

#### Scenario: Checking out a commit
- **WHEN** an operator selects checkout on a specific commit in the graph
- **THEN** the system requires confirmation, then checks out that commit, and the repository's git status subsequently shows a detached HEAD at that commit rather than a branch name

#### Scenario: Detached HEAD is shown distinctly
- **WHEN** a repository's HEAD is detached
- **THEN** its git status summary (in both the repository list and its detail page) indicates the detached state and the commit it's at, not the raw literal value git itself would report for an unnamed ref

### Requirement: Repository detail provides a file tree browser
The system SHALL provide, on a repository's detail subpage, a tree-structured, expandable/collapsible view of that repository's files and folders (rooted at the repository's own directory), in the spirit of a desktop file-manager tree. Folders SHALL be collapsed by default below the top level and load their contents on first expansion rather than the whole tree being loaded up front. Per file or folder, the operator SHALL be able to download (files only), rename, or delete (files and folders, folders recursively) — delete SHALL require explicit confirmation given its irreversibility; rename SHALL not. The system SHALL NOT expose creating new files/folders or uploading content through this browser.

#### Scenario: Expanding a folder
- **WHEN** an operator expands a collapsed folder in the tree
- **THEN** the system loads and displays that folder's immediate contents, without having pre-loaded the rest of the repository's tree

#### Scenario: Downloading a file
- **WHEN** an operator selects download on a file in the tree
- **THEN** the system transfers that file's exact bytes back to the operator's browser as a download

#### Scenario: Renaming a file or folder
- **WHEN** an operator renames a file or folder to a new name within the same parent directory
- **THEN** the system renames it on disk and the tree reflects the new name, without requiring a confirmation step

#### Scenario: Deleting a file or folder requires confirmation
- **WHEN** an operator selects delete on a file or folder
- **THEN** the system requires explicit confirmation before removing it (recursively, for a folder) from disk

#### Scenario: File-browser operations are confined to the repository
- **WHEN** any file-browser operation (list, download, rename, delete) is requested with a path that would resolve outside the named repository's own directory
- **THEN** the system rejects the request without performing any filesystem operation, the same confinement guarantee `file-transfer` provides for the sbx-facing API

#### Scenario: The repository root itself cannot be renamed or deleted through the file browser
- **WHEN** a rename or delete request targets the repository's own root (an empty relative path)
- **THEN** the system rejects it — removing or renaming a repository is `repository-management`'s own dedicated deletion action, not a file-browser operation

### Requirement: Deletion and the new git operations are reachable only from the authenticated dashboard; listing and cloning are also REST-reachable
The system SHALL expose repository listing and cloning as bearer-token REST endpoints (`GET /repositories`, `POST /repositories/clone`) reachable by the sbx sandbox caller, per `dotnet-command-execution`'s authentication model — this was deliberately widened after this requirement's original "dashboard-only" scope (see `design.md`). Deletion, the pull/push/force-push/fetch/clean/branch-switch actions, the commit graph and commit checkout, and the file tree browser's download/rename/delete SHALL NOT be exposed as bearer-token REST endpoints — they remain available only as authenticated in-process operations (or, for file download specifically, a dashboard-cookie-authenticated endpoint distinct from the bearer-token API surface) within the Blazor dashboard, gated by the same dashboard authentication as everything else in `service-dashboard`. This keeps every irreversible or history-rewriting action (delete, force-push, clean, file delete, commit checkout) behind a human operator's explicit dashboard confirmation, unreachable to an sbx caller that "might misuse it."

#### Scenario: List and clone are REST-reachable
- **WHEN** a caller presents a valid bearer credential to `GET /repositories` or `POST /repositories/clone`
- **THEN** the system lists repositories or starts a clone, the same as the equivalent dashboard action would

#### Scenario: No REST endpoint exists for delete or the new git operations
- **WHEN** a caller presents a valid bearer credential to the service's command/file API surface
- **THEN** no request against that API surface can delete a repository, pull, push, force-push, fetch, clean, or switch its branch — those actions exist only inside the authenticated dashboard
