# Out of the Box

*(repository/codebase name: `OutOfTheBox`)*

A Windows-hosted service that lets a Claude Code instance running in a remote sbx sandbox run `dotnet`/`git` commands, transfer files, and list/clone repositories on this host, since the sandbox has no local .NET toolchain.

## Status

Feature-complete and packaged as a WiX Toolset installer ([`INSTALL.md`](INSTALL.md)); only manual end-to-end verification on a real, clean Windows machine remains (Section 19 of `tasks.md`). See [`openspec/changes/sbx-dotnet-command-service/tasks.md`](openspec/changes/sbx-dotnet-command-service/tasks.md) for the current checklist and [`design.md`](openspec/changes/sbx-dotnet-command-service/design.md) for the full architecture rationale.

## What it does

- Accepts a `dotnet` or `git` command (arguments + working directory) over HTTP, authenticated by a shared bearer credential, and streams stdout/stderr back over Server-Sent Events as the command runs.
- Confines execution to a configured root directory; runs against different repositories in parallel, serializes commands against the same repository, and supports cancellation.
- Transfers a single build-produced file back to the caller, confined to the same repository.
- Persists run history (command, output, outcome, resource usage) to SQLite, browsable via a live-updating Blazor Server dashboard alongside host/process resource monitoring and repository management (list/clone/delete, plus REST endpoints for list/clone so the sbx caller can reach those two directly — delete stays dashboard-only).
- Ships as a self-contained single-file executable, packaged by a WiX Toolset installer (a Burn bootstrapper chaining the .NET SDK/Git for Windows prerequisites ahead of an MSI) with native upgrade/uninstall support.

## How it works

A caller (normally the sbx-side Claude Code instance, see below) sends an HTTPS request carrying a
shared bearer token and either a `dotnet`/`git` argument list plus a repository-relative working
directory, or a request for a specific file inside a repository. The service checks the token, resolves
the working directory against a configured root directory (rejecting anything that escapes it —
`../`, an absolute path, a symlink that resolves outside), and, for a command run, spawns
`dotnet.exe`/`git.exe` directly (no shell, so no shell-injection surface) with that resolved
directory as its working directory. Commands against different repositories run in parallel; a second
command against a repository that already has one in flight is rejected outright, not queued, so a caller
always knows immediately whether its request is actually running.

Once a command starts, its `stdout`/`stderr` stream back to the caller incrementally over
Server-Sent Events as the process produces them (not buffered until it exits), ending with a final
event carrying the exit code — so a multi-minute `dotnet test` run shows live progress instead of a
long silent wait. A file-transfer request instead streams a single file's raw bytes back,
confined to that specific repository's own directory (a second, narrower check than the working-directory
confinement above). Every run of every kind — `dotnet` command, `git` command, file transfer,
repository clone, repository delete — is durably recorded to a local SQLite database (arguments or
file path, timestamps, outcome, full output, and a CPU/RAM time series sampled while it ran), which
is what the dashboard's History view reads from, and what lets an interrupted run be reconciled to a
sensible terminal state if the service restarts mid-run.

## Architecture

Clean Architecture (Onion-style): `Domain` at the center (entities, and pure business rules like
"is this already-resolved path contained under that already-resolved root," with zero framework/IO
dependency — not even a NuGet package beyond the BCL) ← `Application` wrapping it (the ports/
interfaces `Infrastructure` implements, like `IProcessRunner` and `IRunRepository`, plus the
services that orchestrate them — run start/cancel, the per-repository lock registry) ← an outer ring split
into two genuinely independent slices, `Infrastructure` (real process spawning, WMI, EF Core/SQLite,
`PerformanceCounter`) and `Presentation` (the minimal API endpoints and the Blazor Server dashboard),
which depend inward on `Application` but **never reference each other, with no exception** ← a fifth
project, `Host`, sitting outside all three rings as the sole composition root: the only project
referencing both outer-ring slices, wiring them together via dependency injection and running the
actual process. `Presentation` has no reference to `Infrastructure` at all, not even for DI — that's
the entire point of `Host` existing as its own project rather than following the common ASP.NET Core
pattern where the web project's `Program.cs` doubles as both presentation layer and composition
root.

This is more structure than a service this size strictly needs, and it's deliberate: it buys two
concrete things beyond looking tidy. First, `Domain`/`Application` logic (path confinement, lock
acquisition, CPU aggregation math, ...) becomes unit-testable without spinning up EF Core, WMI,
`PerformanceCounter`, or Kestrel at all — most of the business logic that actually matters can be
exercised with plain, fast, isolated tests. Second, the layer boundary is mechanically enforced by
`tests/OutOfTheBox.ArchitectureTests` (via NetArchTest) on every `dotnet test` run, not just
documented in a doc a future change can quietly violate — a `Domain → Infrastructure` reference (or
`Presentation → Infrastructure`) fails the build, not a code review comment. See `design.md`'s
Architecture section for the full per-project breakdown and rationale.

## Accessing the dashboard

The dashboard is a human-facing web UI served by the same running service, at
`https://<host>:5443/` by default (the port is whatever was configured at install time — see
[`INSTALL.md`](INSTALL.md)). Since the certificate is typically self-signed for this kind of
deployment, your browser will show a certificate warning the first time — accept/trust it the way
you normally would for a self-signed cert on a private network. You'll land on a login page; enter
the same shared bearer token the API uses (there's no separate dashboard credential) to get a
session cookie.

Once logged in, three top-level views are available:

- **Status** — every run currently in flight (any kind), host CPU/RAM, and the live process tree
  each command run spawned, with a kill button per process.
- **History** — every past run, filterable by kind/outcome/repository and free-text searchable, with a
  per-run detail page showing full output and its resource-usage graph.
- **Repositories** — every repository under the configured root, with live size/git-status/active state;
  clone a new one in or delete an existing one directly from here. Listing and cloning are also
  reachable via REST (`GET /repositories`, `POST /repositories/clone`), so the sbx-side caller can
  do those two itself; deletion is dashboard-only, by design — there's no API for it at all.

## Running a Claude Code instance in the sbx sandbox

The sbx-side Claude Code instance is the caller this service exists for — it talks to the six REST
endpoints ([`skills/dotnet-command-service/SKILL.md`](skills/dotnet-command-service/SKILL.md) is the
authoritative client guide, restating the auth/streaming/cancellation contract with worked `curl`
examples). To get it working:

1. **Have the service installed and running** on a reachable Windows host — see
   [`INSTALL.md`](INSTALL.md). Note the bearer token (auto-generated at install time, shown in the
   installer's config page or resolvable from `HKLM\SOFTWARE\OutOfTheBox` on the host) and the
   host's address/port.
2. **Copy the skill into the sandbox's Claude Code environment.** The skill is authored and shipped
   in this repository, but installing it into a *different* machine's environment is a manual step this
   repository can't do for you — copy the `skills/dotnet-command-service/` directory (containing
   `SKILL.md`) into wherever the sbx sandbox's Claude Code instance looks for skills.
3. **Give the sandbox the token and base URL**, however your sandbox setup passes configuration in
   (e.g. as environment variables) — the skill's own examples assume `$TOKEN` and `$BASE_URL` are
   available for exactly this.
4. **Trust the certificate.** If it's self-signed (typical for this deployment shape), the sandbox
   needs to either pin/trust it explicitly (e.g. `curl --cacert <path-to-cert>`) or the caller needs
   to have independently verified the connection is otherwise safe — see
   [`INSTALL.md`](INSTALL.md#certificate).

From there, the Claude Code instance in the sandbox can start a `dotnet`/`git` run, stream its
output, cancel it, pull back a produced file, and list/clone repositories — exactly as documented in
the skill. Repository *deletion* and the dashboard are both explicitly out of its reach (no REST
surface for either); those stay operator-only, from the dashboard above.

## Documentation

- [`BUILD.md`](BUILD.md) — building and testing this repository
- [`INSTALL.md`](INSTALL.md) — running/deploying the service
- [`CHANGELOG.md`](CHANGELOG.md) — what's changed, release by release
- [`CLAUDE.md`](CLAUDE.md) — project conventions and quick-reference for AI-assisted development in this repository
- [`openspec/changes/sbx-dotnet-command-service/`](openspec/changes/sbx-dotnet-command-service/) — the full proposal, specs, design, and task checklist this project is being built from
