## Why

A Claude Code instance running in an sbx sandbox has no local Windows/.NET toolchain and cannot run `dotnet build`/`test`/`publish` itself, nor manage the state of a git checkout on the Windows host, nor retrieve a build artifact the host produced. It needs a remote Windows host that accepts `dotnet` and `git` commands over the network, executes them, returns the result, and can hand back a produced file, so build/test/source-control feedback loops work from inside the sandbox.

## What Changes

- New Windows-hosted HTTP service that accepts a posted `dotnet` command line from a remote caller and executes it locally via `dotnet.exe`.
- Command execution is generic passthrough: the caller supplies the full argument list for `dotnet`, not a fixed set of endpoints per subcommand.
- A second, structurally identical generic passthrough endpoint accepts a posted `git` command line and executes it via `git.exe` — also unrestricted (no subcommand/flag allowlist, including destructive operations like `reset --hard`), sharing the same per-repo lock, cancellation endpoint, and run-history recording as the `dotnet` endpoint, since both operate on the same checkout.
- A third endpoint transfers a build artifact file from a repo back to the caller, confined to that specific repo's own directory tree (never able to read outside it, including via traversal or symlink escape) — needed because some sbx-side tooling needs the actual output file, not just streamed stdout/stderr.
- Requests must present a bearer/API-key credential; unauthenticated requests are rejected, across all three endpoints plus cancellation.
- Command output streams back incrementally over Server-Sent Events as the command runs, with a final event carrying the exit code (no job queue/poll model for v1).
- `dotnet`/`git` execution is scoped to their respective executables only: the service does not accept or run arbitrary shell commands, only arguments passed to that CLI.
- Working directory / project path handling: caller specifies a repo-relative path; service resolves it under a configured root and rejects paths that escape that root. Artifact transfer applies a second, narrower confinement check: the requested file must resolve within the specific repo directory named, not merely within the configured root.
- Caller may specify a per-request execution timeout, overriding the configured default (10 minutes); the server clamps any caller-supplied value to a configured maximum.
- Commands (`dotnet` or `git`) against different repos run in parallel; commands against the same repo are serialized regardless of kind — only one `dotnet`/`git` command may be in flight per repo at a time, a second request for a busy repo is rejected (not queued). Artifact transfers do not contend for this lock (read-only).
- Caller can cancel an in-flight run (command or transfer) via its run id, through one shared cancellation endpoint; cancellation kills the process (for commands) or stops the stream (for transfers) and releases that repo's lock immediately where applicable.
- Every run of every kind — `dotnet` command, `git` command, or artifact transfer — is persisted to durable storage on the host with its kind, repo, kind-appropriate detail (arguments, or transferred file path/size), timestamps, outcome, and full output where applicable, covering both in-flight and completed runs.
- History is filterable by run kind, outcome, and repository, and searchable by free text, both via the query API and in the dashboard's history view.
- A human-readable web dashboard shows current status (in-flight runs of every kind) and browsable history (past runs of every kind, with full output or transfer metadata as applicable), updating live without a manual page refresh.
- Service ships as a single self-contained executable (bundles its own .NET runtime) with `install.ps1`/`upgrade.ps1` scripts — no separate runtime install on the host, and upgrading is a binary swap that never touches configuration or persisted history.
- Dashboard shows host resource usage (total + per-core CPU, total RAM, the service's own RAM) and a list of processes the service itself spawned (a `dotnet.exe`/`git.exe` run and its children, e.g. `testhost.exe`), each with live CPU/RAM, refreshed every few seconds. Artifact transfers, which spawn no child process, get the same resource-graph treatment sourced from host-level samples instead of a process tree.
- Operator can kill an individual spawned process (and its descendants) directly from that list — for unsticking a hung `dotnet test`/`testhost.exe` without necessarily needing the run's id.
- Dashboard renders in dark mode only (no light-mode toggle) and organizes status, resource/process monitoring, and history into separate views rather than one dense page.
- A Claude Code skill (`SKILL.md`) is authored in this repo documenting how to call the service from the sbx side: auth header, starting/cancelling a `dotnet` or `git` run, requesting an artifact transfer, the timeout-override field, and how to consume the SSE event stream from a Bash-based agent — so the sbx-side Claude Code instance doesn't have to reverse-engineer the API contract from the specs directly.
- Repo-level documentation (`README.md`, `BUILD.md`, `INSTALL.md`, `CHANGELOG.md`), a standard copyright header on every source file, and a `CLAUDE.md` documenting the project for future work sessions.

## Capabilities

### New Capabilities
- `dotnet-command-execution`: accept a posted `dotnet` argument list plus target working directory, run it via `dotnet.exe` under a per-repo lock (parallel across repos, serialized within one repo, shared with `git-command-execution`), stream exit code/stdout/stderr back, and support cancelling an in-flight run.
- `git-command-execution`: the same contract as `dotnet-command-execution`, unrestricted, for `git.exe`, sharing the same per-repo lock and cancellation endpoint.
- `artifact-transfer`: transfer a file from within a specific repo's directory tree back to the caller, confined so it can never read outside that repo, distinguishing a missing file from a confinement violation, and recorded in history like a command run.
- `service-authentication`: require and validate a bearer/API-key credential on every request before executing a command or transferring a file.
- `run-history`: durably persist every run's kind, repo, kind-appropriate detail, timestamps, outcome, and captured output/metadata — from the moment it starts (in-flight) through its terminal state — and make it queryable, filterable (by kind/outcome/repo), and free-text searchable.
- `service-dashboard`: human-readable, live-updating, dark-mode-only web UI showing current in-flight runs of every kind, host/process resource monitoring, and browsable, filterable, searchable run history.
- `host-resource-monitoring`: sample host CPU/RAM and the resource usage of processes the service itself spawned, and let an operator terminate a spawned process (and its descendants) — scoped strictly to the service's own process trees, never arbitrary host processes.

### Modified Capabilities
(none — greenfield project; `git-command-execution` and `artifact-transfer` were added mid-planning, before any implementation of this change existed, so they're additive new capabilities rather than deltas against shipped behavior)

## Impact

- New solution (`.slnx`) added to this repo, following Clean Architecture: `Domain`, `Application`, and two independent outer-ring slices — `Infrastructure` and `Presentation` (ASP.NET Core minimal API + Blazor Server, as a Razor Class Library with no reference to Infrastructure) — with neither slice referencing the other, plus a separate `Host` project (the actual Windows-hosted executable) as the sole composition root wiring them together. `UnitTests`, `BehaviorTests`, `ArchitectureTests`, and fixture test projects sit alongside; the five architecture projects are flat and unfoldered at the solution root, with `Tests` the only solution folder (grouping the three flat test projects). Central Package Management (`Directory.Packages.props`) pins every third-party package version once at the repo root, and a root `.editorconfig` codifies the formatting/style conventions already in use.
- Build/dev machine needs .NET 10 SDK (already installed); the deployed service host does not, since it ships self-contained. The host also needs `git` installed and resolvable on `PATH`, the same assumption already made for `dotnet`.
- Network-exposed surface on the Windows box: an HTTP port must be opened/reachable from the sbx sandbox machine, which is a new attack surface (arbitrary `dotnet`/`git` args from a remote, trusted-but-not-fully-sandboxed caller, plus file reads confined to configured repos) — mitigated by auth + path confinement, not by command allowlisting. Unrestricted `git` specifically adds the same class of risk `dotnet` already carries (git hooks can execute arbitrary code, similar to MSBuild custom targets) — accepted under the same trust boundary, not separately mitigated.
- New durable state on the host: a SQLite database file holding run history (command text or transfer metadata, repo paths, and full command output), kept in a per-machine data directory separate from the install directory — this is new persistent state where previously there was none, and needs its own backup/retention consideration.
- Service account needs local rights to read Windows performance counters (CPU) and to enumerate/terminate its own child processes — still no local-admin requirement, but a slightly larger local-OS permission footprint than before.
- New non-code asset added to this repo: a Claude Code skill directory (e.g. `skills/dotnet-command-service/SKILL.md`) that the sbx sandbox's own Claude Code environment needs to have installed/copied in separately — this repo can't install it into a different machine's environment itself.
- No existing specs/code affected.
