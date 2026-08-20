# Out of the Box

*(repository/codebase name: `OutOfTheBox`)*

A Windows-hosted service that lets a Claude Code instance running in a remote sbx sandbox run `dotnet`/`git` commands, transfer files, and list/clone repositories on this host, since the sandbox has no local .NET toolchain. Two interfaces only: an MCP server for the sbx-side caller, and a Blazor Server web dashboard for the human operator — no client-side skill/doc dependency, since MCP tools are self-describing.

## Status

Released as v1.0.1, packaged as a WiX Toolset installer - grab `OutOfTheBoxSetup.exe` from the [latest release](https://github.com/Cataclysm/OutOfTheBox/releases/latest) or see [`INSTALL.md`](INSTALL.md) for the full walkthrough (including building it yourself). Manual end-to-end verification on a real, clean Windows machine is still outstanding (see the full workflow plan in [`E2ETESTPLAN.md`](E2ETESTPLAN.md)) - treat a fresh install/upgrade/uninstall as not yet independently confirmed beyond this repository's own automated test suite and CI. See [`openspec/changes/archive/2026-08-09-sbx-dotnet-command-service/tasks.md`](openspec/changes/archive/2026-08-09-sbx-dotnet-command-service/tasks.md) for that original checklist and [`design.md`](openspec/changes/archive/2026-08-09-sbx-dotnet-command-service/design.md) for the full architecture rationale; later work lives in its own `openspec/changes/<name>/` (in-flight) or `openspec/changes/archive/` (merged) directories.

## What it does

- Accepts a `dotnet` or `git` command (arguments + working directory) as an MCP tool call (`dotnet_run`/`git_run`), authenticated by a shared bearer credential, and returns a run id immediately - the caller polls `read_run_output` for incremental stdout/stderr and the eventual exit code, since MCP tool calls are fundamentally request/response, not a persistent stream. See [`openspec/changes/archive/2026-08-09-sbx-mcp-server/`](openspec/changes/archive/2026-08-09-sbx-mcp-server/) for the full tool set and design rationale.
- Confines execution to a configured root directory; runs against different repositories in parallel, serializes commands against the same repository, and supports cancellation (`cancel_run`).
- Transfers a single file back to the caller (`transfer_file`), confined to the same repository, size-capped since an MCP tool result is a single response payload.
- Lets the caller diagnose Windows-specific failures itself: `get_run_resources` (a run's recent CPU/RAM trend, to judge whether it's hung or still working), `get_environment_info` (installed dotnet/git toolchain, SDKs, workloads, NuGet sources, disk space), and `get_file_lock_info` (which process(es) have a file open, for a "file in use" build/test failure).
- Persists run history (command, output, outcome, resource usage) to SQLite, browsable via a live-updating Blazor Server dashboard alongside host/process resource monitoring and repository management (list/clone/delete, pull/push/force-push/fetch/clean, branch switching with auto-tracking, plus MCP tools for list/clone so the sbx caller can reach those two directly — everything else stays dashboard-only).
- Ships as a self-contained single-file executable, packaged by a WiX Toolset installer (a Burn bootstrapper chaining the .NET SDK/Git for Windows prerequisites ahead of an MSI) with native upgrade/uninstall support.

## How it works

A caller (normally the sbx-side Claude Code instance, see below) connects to the MCP server over
Streamable HTTP at `/mcp`, presenting a shared bearer token on every request, and calls a tool with
either a `dotnet`/`git` argument list plus a repository-relative working directory, or a request for
a specific file inside a repository. The service checks the token, resolves the working directory
against a configured root directory (rejecting anything that escapes it — `../`, an absolute path, a
symlink that resolves outside), and, for a command run, spawns `dotnet.exe`/`git.exe` directly (no
shell, so no shell-injection surface) with that resolved directory as its working directory.
Commands against different repositories run in parallel; a second command against a repository that
already has one in flight is rejected outright, not queued, so a caller always knows immediately
whether its request is actually running.

`dotnet_run`/`git_run`/`clone_repository` return a run id as soon as the run is accepted, without
waiting for it to finish — the caller polls `read_run_output` (an offset-based cursor into the run's
stdout/stderr) as many times as it wants, including after the run has already finished, to see
incremental progress and the eventual exit code. `transfer_file` is the one synchronous tool - a
file's contents come back directly in the same call, base64-encoded, since the configured size cap
keeps a transfer small enough to always complete within one response. Every run of every kind —
`dotnet` command, `git` command, file transfer, repository clone, repository delete — is durably
recorded to a local SQLite database (arguments or file path, timestamps, outcome, full output, and a
CPU/RAM time series sampled while it ran), which is what the dashboard's History view reads from, and
what lets an interrupted run be reconciled to a sensible terminal state if the service restarts
mid-run.

## Architecture

Clean Architecture (Onion-style): `Domain` at the center (entities, and pure business rules like
"is this already-resolved path contained under that already-resolved root," with zero framework/IO
dependency — not even a NuGet package beyond the BCL) ← `Application` wrapping it (the ports/
interfaces `Infrastructure` implements, like `IProcessRunner` and `IRunRepository`, plus the
services that orchestrate them — run start/cancel, the per-repository lock registry) ← an outer ring split
into two genuinely independent slices, `Infrastructure` (real process spawning, WMI, EF Core/SQLite,
`PerformanceCounter`) and `Presentation` (the MCP tool definitions, the file-download endpoint, and
the Blazor Server dashboard),
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
deployment, your browser will show a certificate warning the first time — accept/trust it for now,
or download it from the About page after logging in and install it properly (see
[`INSTALL.md`](INSTALL.md#certificate) for the full walkthrough) to stop seeing the warning at all.
You'll land on a login page; enter the same shared bearer token the API uses (there's no separate
dashboard credential) to get a session cookie.

Once logged in, five top-level views are available:

- **Status** — every run currently in flight (any kind), host CPU/RAM, and the live process tree
  each command run spawned, with a kill button per process.
- **History** — every past run, filterable by kind/outcome/repository and free-text searchable, with a
  per-run detail page showing full output and its resource-usage graph.
- **Repositories** — every repository under the configured root, with live size/git-status (branch,
  clean/dirty, ahead/behind, or "remote gone" if a tracking branch's remote side was deleted) and
  active state. Clone a new one in (optionally on a specific branch, picked from a dropdown populated
  by querying the remote), delete one, or run pull/push/force-push/fetch/clean directly from the list
  — the last two require an explicit popup confirmation, and none of the five stream output; the
  triggering icon just flashes green or red to report the outcome. A repository's detail page adds its
  clone source URL, full remote list, and a branch-switch dropdown (switching to a remote-only branch
  auto-creates its local tracking branch), plus a **Commits**/**Files** tab group (only one visible at
  a time): Commits is a branch-aware commit graph (lane/connector lines, branch and tag pills, checkout
  any commit as a detached HEAD); Files is an Explorer-style expandable tree rooted at the repository,
  supporting download/rename/delete of any file or folder. Both refresh live — the commit graph
  alongside the page's own git-status refresh, the file tree per expanded folder on its own polling
  interval. Listing and cloning are also reachable via MCP (`list_repositories`, `clone_repository`),
  so the sbx-side caller can do those two itself; everything else (delete, pull/push/force-push/
  fetch/clean, branch switching, commit checkout, the file browser) is dashboard-only, by design —
  there's no MCP tool for any of it.
- **Credentials** — every stored git host and NuGet feed credential (host/feed URL, when authorized,
  health), independent of the MCP tools that can also authorize/revoke them.
- **MCP Settings** — a per-tool and per-`dotnet_run`/`git_run`-subcommand on/off switch for the sbx
  caller, grouped into fieldsets (dotnet subcommands, git subcommands, run management, repository
  management, file management, git/NuGet credentials, diagnostics). Every mutating or credential
  tool defaults to disabled; every read-only or already-vetted tool/subcommand defaults to enabled.
  Each row carries a colored risk icon — a red warning triangle for anything that can destroy data,
  reach outside the repository, or expose a credential; a yellow info mark for anything safe but
  potentially information-revealing (e.g. `list_authorized_git_hosts`); a green check for everything
  else — and hovering or focusing the icon opens a tooltip explaining what the tool/subcommand does,
  how it works, and why it's rated that way. Changes persist to the database and take effect
  immediately, no restart needed; call `get_mcp_permissions` from the sbx side to see the current
  allowed set, since an operator can change it mid-session.

## Running a Claude Code instance in the sbx sandbox

The sbx-side Claude Code instance is the caller this service exists for — it connects as an MCP
client to the server described above. To get it working:

1. **Have the service installed and running** on a reachable Windows host — see
   [`INSTALL.md`](INSTALL.md). Note the bearer token (auto-generated at install time, shown in the
   installer's config page or resolvable from `HKLM\SOFTWARE\OutOfTheBox` on the host) and the
   host's address/port.
2. **Configure the sandbox's Claude Code instance with a remote MCP server**: endpoint
   `https://<host>:<port>/mcp` (Streamable HTTP transport), with an `Authorization: Bearer <token>`
   header set to the token from step 1. Exactly how you register a remote MCP server depends on your
   sandbox's own Claude Code configuration mechanism - nothing repository-specific to copy in, unlike
   a skill: nineteen plain tools (`dotnet_run`, `git_run`, `read_run_output`, `cancel_run`,
   `transfer_file`, `list_repositories`, `clone_repository`, `delete_repository`, `find_files`,
   `get_file_info`, `delete_path`, `get_file_lock_info`, `authorize_git_host`,
   `list_authorized_git_hosts`, `revoke_git_host_authorization`, `authorize_nuget_feed`,
   `list_authorized_nuget_feeds`, `revoke_nuget_feed_authorization`, `get_environment_info`), plus
   `get_mcp_permissions`, are discovered automatically once connected, each with a self-describing
   schema. Which of these — and which `dotnet_run`/`git_run` subcommand — are actually *usable* is
   separately, dynamically operator-controlled from the dashboard's MCP Settings page (see above);
   call `get_mcp_permissions` for the current allowed set rather than assuming, since an operator can
   change it mid-session.
3. **Trust the certificate.** If it's self-signed (typical for this deployment shape), download it
   from the dashboard's About page (`https://<host>:<port>/dashboard-certificate` once logged in)
   and trust it on the sandbox side, or have the caller independently verify the connection is
   otherwise safe — see [`INSTALL.md`](INSTALL.md#certificate) for the full walkthrough (system-wide
   trust, `NODE_EXTRA_CA_CERTS`, or a one-off `curl --cacert` check).

From there, the Claude Code instance in the sandbox can start a `dotnet`/`git` run (subject to
MCP Settings' subcommand allowlist), poll its progress, cancel it, pull back a produced file, and
list/clone repositories — using the tools directly, with no separate client documentation needed.
The dashboard itself stays operator-only regardless of MCP Settings (there's no MCP tool that reaches
it), and a handful of tools (`delete_repository`, `delete_path`, `clone_repository`, the credential
authorize/revoke tools) default to disabled precisely because they're destructive or handle secrets —
an operator has to deliberately opt back in from MCP Settings.

## License

[GNU Affero General Public License v3.0 (AGPL-3.0)](LICENSE) — free to use, modify, and redistribute,
including running a modified version as a hosted service, provided that version's source stays
available under the same license to anyone who interacts with it over a network. See the About
page's own License section (once logged in) for the same summary.

## Documentation

- [`BUILD.md`](BUILD.md) — building and testing this repository
- [`INSTALL.md`](INSTALL.md) — running/deploying the service
- [`CHANGELOG.md`](CHANGELOG.md) — what's changed, release by release
- [`E2ETESTPLAN.md`](E2ETESTPLAN.md) — a full sandbox-realistic end-to-end workflow test plan (not yet executed) to run against a real deployed instance
- [`CLAUDE.md`](CLAUDE.md) — project conventions and quick-reference for AI-assisted development in this repository
- [`openspec/`](openspec/) — `specs/` for the current canonical behavior contracts, `changes/` for each change's own proposal/specs/design/tasks (in-flight or, once merged, under `changes/archive/`)
