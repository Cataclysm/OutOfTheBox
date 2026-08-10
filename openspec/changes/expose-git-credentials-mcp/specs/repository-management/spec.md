## MODIFIED Requirements

### Requirement: Repositories are listed with identifying stats
The system SHALL enumerate every top-level directory under the configured root as a repository and report, for each: its name, total on-disk size, git status summary (current branch, clean/dirty, ahead/behind its upstream if one is configured, or a distinct "remote branch gone" indication if a tracking branch was configured but its remote-side branch no longer exists — or an indication that it isn't a git repository at all), whether it is currently active (holds the per-repository command lock per `dotnet-command-execution`/`git-command-execution`), and whether its remote host currently appears to need a working credential (per this capability's needs-credential tracking, below). Its clone source URL (if known) and its configured remotes (name and URL) SHALL additionally be shown on its detail subpage (per `service-dashboard`'s "Repository detail is its own subpage").

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

#### Scenario: A repository whose host needs a credential is marked next to its name
- **WHEN** a repository's `origin` remote resolves to a host that currently appears to need a working credential
- **THEN** the repository is shown with a distinct symbol next to its name, in both the repository list and its detail page

#### Scenario: A repository with no evidence of a credential problem is not marked
- **WHEN** a repository's `origin` remote resolves to a host with no recorded authentication failure more recent than its last recorded success (including a host nothing has ever recorded either way)
- **THEN** the repository is shown without the needs-credential symbol

### Requirement: A new repository can be cloned, optionally on a specific initial branch
The system SHALL let an operator clone a new repository by supplying a source URL, a name, and an optional initial branch, resolve the name under the configured root (rejecting a name that would escape the root or that already exists), and run `git clone <url>` (with `--branch <branch>` appended when one was supplied) targeting that resolved, not-yet-existing directory — streaming its output the same way `git-command-execution` streams output, assigning it a run id, and recording it in history (per `run-history`) with a distinct kind. This branch parameter SHALL be accepted both from the dashboard's clone dialog and from the `clone_repository` MCP tool. When the dashboard's clone dialog starts a clone, it SHALL remain open and watch that clone until it reaches a terminal state, rather than closing immediately once the clone is accepted.

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
- **WHEN** an operator (or an MCP caller via `clone_repository`) supplies a branch name along with the URL and name
- **THEN** the system runs `git clone --branch <branch> <url>` and the cloned repository is checked out on that branch

#### Scenario: Cloning without a branch uses the remote's default
- **WHEN** no branch is supplied
- **THEN** the system runs a plain `git clone` and the cloned repository is checked out on the remote's default branch, as before this capability existed

#### Scenario: A clone that fails for a non-authentication reason is reported in-dialog
- **WHEN** a clone started from the dashboard's clone dialog reaches a failed terminal state for a reason other than authentication
- **THEN** the dialog reports the failure with its detail, rather than having already closed with no indication anything went wrong

#### Scenario: A clone requiring authentication prompts for a PAT and retries automatically
- **WHEN** a clone started from the dashboard's clone dialog fails in a way the system classifies as an authentication failure
- **THEN** the dialog prompts the operator for a personal access token for that host, stores it the same way `authorize_git_host` does, and automatically retries the same clone once a token verifies

#### Scenario: A PAT that does not work is rejected with feedback, and the operator is re-prompted
- **WHEN** an operator supplies a personal access token in response to a clone's authentication-failure prompt and it does not verify
- **THEN** the system informs the operator that the token did not work and re-prompts for another, repeating until a token verifies or the operator cancels

#### Scenario: Cancelling the PAT prompt means no clone happens
- **WHEN** an operator cancels the personal-access-token prompt shown during a clone
- **THEN** the clone is not retried again and no repository is left behind from that attempt

### Requirement: Dashboard-only pull/push/force-push/fetch/clean actions per repository
The system SHALL let an operator trigger `git pull`, `git push`, `git push --force`, `git fetch`, and `git clean -xdf` against a repository directly from the Repositories list, each acquiring the repository's per-repository command lock (rejected the same way any other command would be if the repository is busy) for the duration of the operation. These actions SHALL NOT stream live output to the operator — the dashboard SHALL indicate only the outcome (success or failure) once the operation completes, by briefly recoloring the action's icon (green on success, red on failure) rather than opening a console/output view. Force-push and clean SHALL require explicit confirmation before proceeding, per the same confirmation mechanism as deletion. A failure, including one the system classifies as an authentication failure, SHALL NOT open a prompt or dialog of any kind — these actions stay a passive outcome indicator, with failure detail available on demand rather than interrupting the operator.

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

#### Scenario: A failed action's detail is available without an interrupting prompt
- **WHEN** any of these actions fails, for an authentication reason or any other
- **THEN** the failed action's real error detail is available on the action itself (e.g. on hover), and no popup or dialog opens as a result of the failure

## ADDED Requirements

### Requirement: A repository's git credential can be changed from the dashboard
The system SHALL offer a dashboard action, alongside the existing per-repository actions on both the Repositories list and repository detail page, that lets an operator authorize or replace the git credential for the repository's own remote host - resolving the host from the repository's `origin` remote and storing a supplied personal access token the same way `authorize_git_host` does. A repository with no `origin` remote, or one whose URL cannot be resolved to a host, SHALL report that rather than silently doing nothing.

#### Scenario: Changing a repository's credential
- **WHEN** an operator selects the change-credential action for a repository with a resolvable `origin` host and supplies a personal access token that verifies
- **THEN** the credential for that host is stored, replacing any existing one for the same host

#### Scenario: A repository with no resolvable remote host
- **WHEN** an operator selects the change-credential action for a repository with no `origin` remote, or whose remote URL cannot be resolved to a host
- **THEN** the system reports that no host could be determined, rather than opening a prompt with nothing to save against

### Requirement: A host's needs-credential state reflects the most recent network-touching git operation against it
The system SHALL record, per remote host, whether the most recent pull, push, force-push, fetch, or clone that targeted it succeeded or failed for an authentication reason, and SHALL derive that host's needs-credential state from whichever was more recent. `git clean` (which never touches the network) and arbitrary `git_run` commands SHALL NOT update this state - only the operations in this capability with an unambiguous single target host do.

#### Scenario: A successful operation clears a prior failure
- **WHEN** a host's most recent recorded outcome was an authentication failure, and a subsequent pull, push, force-push, fetch, or clone against that same host succeeds
- **THEN** the host no longer needs a credential, and every repository whose `origin` resolves to it stops showing the needs-credential symbol

#### Scenario: A failure marks the host even without a prior explicit authorization
- **WHEN** a pull, push, force-push, fetch, or clone fails for an authentication reason against a host that was never explicitly authorized via `authorize_git_host` or the change-credential action
- **THEN** the host is recorded as needing a credential regardless
