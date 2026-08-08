# Changelog

All notable changes to this project are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project has not yet reached a first release, so everything so far is under `Unreleased`.

## [Unreleased]

### Added

- Solution scaffolding: `.slnx`, five-project Clean Architecture (`Domain`/`Application`/`Infrastructure`/`Presentation`/`Host`), `Directory.Build.props` enforcing nullable reference types and mandatory XML doc comments on public API
- Test suite foundation: `UnitTests` (xUnit), `BehaviorTests` (Reqnroll + `WebApplicationFactory`), `ArchitectureTests` (NetArchTest, mechanically enforcing the Clean Architecture boundary), and standalone fixture repos (`PassingFixture`/`FailingFixture`/`HangingFixture`/`GitFixture`) for BDD scenarios to run real `dotnet`/`git` commands against
- Bearer authentication: constant-time credential comparison, a reusable minimal-API endpoint filter, shared by the command API and the dashboard (exchanged for an auth cookie via a login page)
- Path confinement: caller-supplied working directories are canonicalized, symlink-resolved, and confined to a configured root directory; artifact downloads apply the same policy twice (root→repo, then repo→file)
- Command execution: `POST /run` streams `dotnet` command output over Server-Sent Events (`stdout`/`stderr`/`done`/`error` events), with a caller-overridable execution timeout (clamped to a configured maximum) and an output size cap
- Per-repo concurrency locking: commands against different repos run in parallel; a second command against a repo that already has one in flight is rejected with the conflicting run's id, not queued
- Git command execution: `POST /run/git` mirrors `POST /run` (same auth/SSE/timeout/locking), deliberately with no subcommand/flag allowlist; shares the per-repo lock and cancel endpoint bidirectionally with `dotnet` commands
- Cancellation: `POST /run/{runId}/cancel` for any in-flight run, killing the full process tree
- Artifact transfer: `POST /artifacts` streams a single file's raw bytes back to the caller, confined to the named repo's own directory
- SQLite-persisted run history (via EF Core) for every run kind, including full-duration CPU/RAM time series per run, surviving service restarts with startup reconciliation of interrupted runs, filterable/searchable across kind/outcome/repo
- Repository management: dashboard-only (no REST surface) list/clone/delete of repositories under the configured root, with live size/git-status/active stats sampled on a background cadence
- Blazor Server dashboard: dark-mode-only, bearer-token login, Status/History/Repos top-level views plus routable run-detail/repo-detail subpages, a spawned-process list scoped to each run's own process tree with a kill action, history/repo filter and free-text search
- Host and per-process resource monitoring (`PerformanceCounter`/WMI): host-level CPU/RAM plus per-process CPU/RAM for every process in a run's tree
- Live and historical performance graphs (vendored Chart.js): a rolling window during a run, the full persisted series afterward
- Transport hardening: HTTPS-only Kestrel (the service refuses to start on a plain-HTTP endpoint), SSE response buffering disabled for immediate flush
- Product branding: a logo (terminal-prompt `>` chevron on an orange-to-pink gradient), applied consistently across the installer (icon, banner/dialog art, bootstrapper logo) and the dashboard (favicon, header/login logo, accent color, run-kind badge colors)
- WiX Toolset installer: a Burn bootstrapper (`OutOfTheBoxSetup.exe`) chaining a hash-verified .NET 10 SDK and Git for Windows ahead of the MSI; the MSI creates a dedicated least-privilege service account, registers the Windows Service with SCM crash-recovery, opens a firewall rule, and natively supports upgrade/uninstall while structurally preserving the data directory (config + SQLite file) across both; an interactive config page (repo root, bearer token, port) with an auto-generated, upgrade-preserved, operator-overridable bearer token and service-account password, version display (including "upgrading from vX.Y.Z" on upgrades), and a dark theme on its two fully-owned dialog pages within native MSI's real styling limits
- A Claude Code skill (`skills/dotnet-command-service/SKILL.md`) documenting the API for the sbx-side caller: auth, `dotnet`/`git` start/cancel, artifact transfer, SSE consumption pattern, error meanings — repository management is out of scope since it has no REST surface

### Changed

- Flattened `src/` and `tests/` project directories (removed the layer-name/test-type wrapper folders, e.g. `src/Domain/OutOfTheBox.Domain/` → `src/OutOfTheBox.Domain/`); the `.slnx` no longer has per-layer solution folders, only a single `Tests` folder grouping the test projects
- Adopted Central Package Management (`Directory.Packages.props`) for third-party package versions (the `installer/` tree opts out, since WiX's own package versioning conventions don't fit CPM cleanly)
- Adopted the .NET SDK's centralized artifacts output layout: every project under `src/`/`tests/` (including `tests/Fixtures/`) builds into a single `artifacts/` directory at the repo root instead of a per-project `bin/`/`obj/` pair
- `.editorconfig` expanded from formatting-only conventions to a curated set of modern-C#-construct style rules (pattern matching, target-typed `new`, collection expressions, readonly/accessibility modifiers, unused usings/parameters, ...), enforced at `warning` severity and surfaced during `dotnet build`
- Renamed the project from `BuildAndTestService` to `OutOfTheBox`
- Replaced the originally-planned `install.ps1`/`upgrade.ps1` scripts with the WiX Toolset installer above, on explicit request — native upgrade/uninstall semantics and a proper config UI a hand-rolled script couldn't match

### In progress

Manual end-to-end verification of the installer on a real, clean Windows machine (no .NET SDK, no git) — see [`openspec/changes/sbx-dotnet-command-service/tasks.md`](openspec/changes/sbx-dotnet-command-service/tasks.md) Section 19 for the outstanding checklist: fresh install, upgrade, uninstall, and crash-recovery, plus confirming whether the MSI's config page displays when installed through the bootstrapper (vs. the MSI run directly).
