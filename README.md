# OutOfTheBox

A Windows-hosted service that lets a Claude Code instance running in a remote sbx sandbox run `dotnet build`/`test`/etc. against repos on this host, since the sandbox has no local .NET toolchain.

## Status

In active development. Not yet deployable — packaging, install scripts, and the dashboard are still being built. See [`openspec/changes/sbx-dotnet-command-service/tasks.md`](openspec/changes/sbx-dotnet-command-service/tasks.md) for the current checklist and [`design.md`](openspec/changes/sbx-dotnet-command-service/design.md) for the full architecture rationale.

## What it does (once complete)

- Accepts a `dotnet` command (arguments + working directory) over HTTP, authenticated by a shared bearer credential, and streams stdout/stderr back over Server-Sent Events as the command runs.
- Confines execution to a configured root directory; runs against different repos in parallel, serializes commands against the same repo, and supports cancellation.
- Persists run history (command, output, outcome, resource usage) to SQLite, browsable via a live-updating Blazor Server dashboard alongside host/process resource monitoring.
- Ships as a self-contained single-file executable with PowerShell install/upgrade scripts.

## Architecture

Clean Architecture: `Domain` (no dependencies) ← `Application` ← two independent outer-ring slices, `Infrastructure` and `Presentation` (never reference each other) ← `Host` (the sole composition root, the only project referencing everything). The boundary is enforced by `ArchitectureTests` (NetArchTest), not just documented. See `design.md`'s Architecture section for the full rationale.

## Documentation

- [`BUILD.md`](BUILD.md) — building and testing this repo
- [`INSTALL.md`](INSTALL.md) — running/deploying the service
- [`CHANGELOG.md`](CHANGELOG.md) — what's changed, release by release
- [`CLAUDE.md`](CLAUDE.md) — project conventions and quick-reference for AI-assisted development in this repo
- [`openspec/changes/sbx-dotnet-command-service/`](openspec/changes/sbx-dotnet-command-service/) — the full proposal, specs, design, and task checklist this project is being built from
