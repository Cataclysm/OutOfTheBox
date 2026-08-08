# OutOfTheBox

A Windows-hosted service that lets a Claude Code instance running in a remote sbx sandbox run `dotnet`/`git` commands and transfer build artifacts against repos on this host, since the sandbox has no local .NET toolchain.

## Status

Feature-complete and packaged as a WiX Toolset installer ([`INSTALL.md`](INSTALL.md)); only manual end-to-end verification on a real, clean Windows machine remains (Section 19 of `tasks.md`). See [`openspec/changes/sbx-dotnet-command-service/tasks.md`](openspec/changes/sbx-dotnet-command-service/tasks.md) for the current checklist and [`design.md`](openspec/changes/sbx-dotnet-command-service/design.md) for the full architecture rationale.

## What it does

- Accepts a `dotnet` or `git` command (arguments + working directory) over HTTP, authenticated by a shared bearer credential, and streams stdout/stderr back over Server-Sent Events as the command runs.
- Confines execution to a configured root directory; runs against different repos in parallel, serializes commands against the same repo, and supports cancellation.
- Transfers a single build-produced file back to the caller, confined to the same repo.
- Persists run history (command, output, outcome, resource usage) to SQLite, browsable via a live-updating Blazor Server dashboard alongside host/process resource monitoring and repository management (clone/delete/list, dashboard-only).
- Ships as a self-contained single-file executable, packaged by a WiX Toolset installer (a Burn bootstrapper chaining the .NET SDK/Git for Windows prerequisites ahead of an MSI) with native upgrade/uninstall support.

## Architecture

Clean Architecture: `Domain` (no dependencies) ← `Application` ← two independent outer-ring slices, `Infrastructure` and `Presentation` (never reference each other) ← `Host` (the sole composition root, the only project referencing everything). The boundary is enforced by `ArchitectureTests` (NetArchTest), not just documented. See `design.md`'s Architecture section for the full rationale.

## Documentation

- [`BUILD.md`](BUILD.md) — building and testing this repo
- [`INSTALL.md`](INSTALL.md) — running/deploying the service
- [`CHANGELOG.md`](CHANGELOG.md) — what's changed, release by release
- [`CLAUDE.md`](CLAUDE.md) — project conventions and quick-reference for AI-assisted development in this repo
- [`openspec/changes/sbx-dotnet-command-service/`](openspec/changes/sbx-dotnet-command-service/) — the full proposal, specs, design, and task checklist this project is being built from
