# Changelog

All notable changes to this project are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project has not yet reached a first release, so everything so far is under `Unreleased`.

## [Unreleased]

### Added

- Solution scaffolding: `.slnx`, five-project Clean Architecture (`Domain`/`Application`/`Infrastructure`/`Presentation`/`Host`), `Directory.Build.props` enforcing nullable reference types and mandatory XML doc comments on public API
- Test suite foundation: `UnitTests` (xUnit), `BehaviorTests` (Reqnroll + `WebApplicationFactory`), `ArchitectureTests` (NetArchTest, mechanically enforcing the Clean Architecture boundary), and three standalone fixture repos (`PassingFixture`/`FailingFixture`/`HangingFixture`) for BDD scenarios to run real `dotnet` commands against
- Bearer authentication: constant-time credential comparison, a reusable minimal-API endpoint filter
- Path confinement: caller-supplied working directories are canonicalized, symlink-resolved, and confined to a configured root directory
- Command execution: `POST /run` streams `dotnet` command output over Server-Sent Events (`stdout`/`stderr`/`done`/`error` events), with a caller-overridable execution timeout (clamped to a configured maximum) and an output size cap
- Per-repo concurrency locking: commands against different repos run in parallel; a second command against a repo that already has one in flight is rejected with the conflicting run's id, not queued

### Changed

- Flattened `src/` and `tests/` project directories (removed the layer-name/test-type wrapper folders, e.g. `src/Domain/OutOfTheBox.Domain/` → `src/OutOfTheBox.Domain/`); the `.slnx` no longer has per-layer solution folders, only a single `Tests` folder grouping the three test projects
- Adopted Central Package Management (`Directory.Packages.props`) for third-party package versions
- Added `.editorconfig` for formatting and C# style conventions

### In progress

See [`openspec/changes/sbx-dotnet-command-service/tasks.md`](openspec/changes/sbx-dotnet-command-service/tasks.md) for the live checklist: cancellation API, run history persistence, the Blazor Server dashboard, host/process resource monitoring, performance graphs, transport hardening, packaging/install scripts, and the Claude Code skill for the sbx-side caller are not yet built.
