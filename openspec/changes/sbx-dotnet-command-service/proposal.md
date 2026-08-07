## Why

A Claude Code instance running in an sbx sandbox has no local Windows/.NET toolchain and cannot run `dotnet build`/`test`/`publish` itself. It needs a remote Windows host that accepts a `dotnet` command over the network, executes it, and returns the result, so build/test feedback loops work from inside the sandbox.

## What Changes

- New Windows-hosted HTTP service that accepts a posted `dotnet` command line from a remote caller and executes it locally via `dotnet.exe`.
- Command execution is generic passthrough: the caller supplies the full argument list for `dotnet`, not a fixed set of endpoints per subcommand.
- Requests must present a bearer/API-key credential; unauthenticated requests are rejected.
- Output streams back incrementally over Server-Sent Events as the command runs, with a final event carrying the exit code (no job queue/poll model for v1).
- Execution is scoped to `dotnet.exe` only: the service does not accept or run arbitrary shell commands, only arguments passed to the `dotnet` CLI.
- Working directory / project path handling: caller specifies a repo-relative path; service resolves it under a configured root and rejects paths that escape that root.
- Caller may specify a per-request execution timeout, overriding the configured default (10 minutes); the server clamps any caller-supplied value to a configured maximum.
- Commands against different repos run in parallel; commands against the same repo are serialized — only one `dotnet` command may be in flight per repo at a time, a second request for a busy repo is rejected (not queued).
- Caller can cancel an in-flight command via its run id; cancellation kills the process and releases that repo's lock immediately.
- Every run (its command, repo, timestamps, outcome, and full stdout/stderr) is persisted to durable storage on the host, covering both in-flight and completed runs.
- A human-readable web dashboard shows current status (in-flight runs) and browsable history (past runs, with full output), updating live without a manual page refresh.
- Service ships as a single self-contained executable (bundles its own .NET runtime) with `install.ps1`/`upgrade.ps1` scripts — no separate runtime install on the host, and upgrading is a binary swap that never touches configuration or persisted history.
- Dashboard shows host resource usage (total + per-core CPU, total RAM, the service's own RAM) and a list of processes the service itself spawned (a `dotnet.exe` run and its children, e.g. `testhost.exe`), each with live CPU/RAM, refreshed every few seconds.
- Operator can kill an individual spawned process (and its descendants) directly from that list — for unsticking a hung `dotnet test`/`testhost.exe` without necessarily needing the run's id.
- Dashboard renders in dark mode only (no light-mode toggle) and organizes status, resource/process monitoring, and history into separate views rather than one dense page.
- A Claude Code skill (`SKILL.md`) is authored in this repo documenting how to call the service from the sbx side: auth header, starting/cancelling a run, the timeout-override field, and how to consume the SSE event stream from a Bash-based agent — so the sbx-side Claude Code instance doesn't have to reverse-engineer the API contract from the specs directly.
- Repo-level documentation (`README.md`, `BUILD.md`, `INSTALL.md`, `CHANGELOG.md`), a standard copyright header on every source file, and a `CLAUDE.md` documenting the project for future work sessions.

## Capabilities

### New Capabilities
- `dotnet-command-execution`: accept a posted `dotnet` argument list plus target working directory, run it via `dotnet.exe` under a per-repo lock (parallel across repos, serialized within one repo), stream exit code/stdout/stderr back, and support cancelling an in-flight run.
- `service-authentication`: require and validate a bearer/API-key credential on every request before executing a command.
- `run-history`: durably persist every run's command, repo, timestamps, outcome, and captured output — from the moment it starts (in-flight) through its terminal state — and make it queryable.
- `service-dashboard`: human-readable, live-updating, dark-mode-only web UI showing current in-flight runs, host/process resource monitoring, and browsable run history.
- `host-resource-monitoring`: sample host CPU/RAM and the resource usage of processes the service itself spawned, and let an operator terminate a spawned process (and its descendants) — scoped strictly to the service's own process trees, never arbitrary host processes.

### Modified Capabilities
(none — greenfield project)

## Impact

- New solution (`.slnx`) added to this repo, following Clean Architecture: `Domain`, `Application`, and two independent outer-ring slices — `Infrastructure` and `Presentation` (ASP.NET Core minimal API + Blazor Server, as a Razor Class Library with no reference to Infrastructure) — with neither slice referencing the other, plus a separate `Host` project (the actual Windows-hosted executable) as the sole composition root wiring them together. `UnitTests`, `BehaviorTests`, `ArchitectureTests`, and fixture test projects sit alongside; the five architecture projects are flat and unfoldered at the solution root, with `Tests` the only solution folder (grouping the three flat test projects). Central Package Management (`Directory.Packages.props`) pins every third-party package version once at the repo root, and a root `.editorconfig` codifies the formatting/style conventions already in use.
- Build/dev machine needs .NET 10 SDK (already installed); the deployed service host does not, since it ships self-contained.
- Network-exposed surface on the Windows box: an HTTP port must be opened/reachable from the sbx sandbox machine, which is a new attack surface (arbitrary `dotnet` args from a remote, trusted-but-not-fully-sandboxed caller) — mitigated by auth + path confinement, not by command allowlisting.
- New durable state on the host: a SQLite database file holding run history (command text, repo paths, and full command output), kept in a per-machine data directory separate from the install directory — this is new persistent state where previously there was none, and needs its own backup/retention consideration.
- Service account needs local rights to read Windows performance counters (CPU) and to enumerate/terminate its own child processes — still no local-admin requirement, but a slightly larger local-OS permission footprint than before.
- New non-code asset added to this repo: a Claude Code skill directory (e.g. `skills/dotnet-command-service/SKILL.md`) that the sbx sandbox's own Claude Code environment needs to have installed/copied in separately — this repo can't install it into a different machine's environment itself.
- No existing specs/code affected.
