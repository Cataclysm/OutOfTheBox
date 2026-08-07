# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A Windows-hosted service that lets a Claude Code instance running in a remote sbx sandbox run `dotnet build`/`test`/etc. against repos on this host over HTTP (SSE-streamed output), since the sandbox has no local .NET toolchain. Windows-only throughout (Windows Service hosting, `PerformanceCounter`, WMI).

The authoritative source for *why* and *what's left* is `openspec/changes/sbx-dotnet-command-service/`: `proposal.md` (what/why), `specs/*/spec.md` (behavior contracts), `design.md` (architecture rationale and every non-obvious technical decision), `tasks.md` (the live, checkbox-tracked implementation plan). Read `design.md` before making a design-level change — it documents *why* each decision was made, including alternatives that were rejected and why.

## Commands

```
dotnet build OutOfTheBox.slnx          # build everything
dotnet test OutOfTheBox.slnx           # run everything (slow - see below)

# Fast, run these during normal development:
dotnet test tests/OutOfTheBox.UnitTests/OutOfTheBox.UnitTests.csproj
dotnet test tests/OutOfTheBox.ArchitectureTests/OutOfTheBox.ArchitectureTests.csproj

# Slow (spawns real dotnet.exe processes, includes two deliberate-timeout scenarios):
dotnet test tests/OutOfTheBox.BehaviorTests/OutOfTheBox.BehaviorTests.csproj

# Single test / single scenario:
dotnet test <project.csproj> --filter "FullyQualifiedName~ClassName.MethodName"
```

No Visual Studio dependency anywhere (`.slnx` opens in Rider; Reqnroll's BDD tests run via plain `dotnet test`). See `BUILD.md` for the full test-project breakdown.

## Architecture

Clean Architecture (Onion-style), five projects, dependencies point inward only:

```
Domain (no deps) <- Application <- Infrastructure  (independent slice)
                                 <- Presentation     (independent slice)
                                 <- Host (composition root, references all four)
```

- **`Domain`** — entities and pure business rules with zero framework/IO dependency (not even a NuGet package beyond the BCL). If you can write a fact about the business without mentioning ASP.NET Core, EF Core, or the filesystem, it goes here. Example: `PathConfinementPolicy.IsContained(root, candidate)` takes two already-resolved strings and returns a bool — the actual `Path.GetFullPath`/symlink resolution is Infrastructure's job.
- **`Application`** — ports (interfaces) that `Infrastructure` implements (`IProcessRunner`, `IWorkingDirectoryResolver`, ...), the services that orchestrate them (`RunRegistry`), and shared configuration-shape types (`ServiceOptions`). Depends only on `Domain`.
- **`Infrastructure`** — concrete implementations of `Application`'s ports: real process spawning, WMI, `PerformanceCounter`, EF Core/SQLite (once built). `net10.0-windows` (the others besides `Host` are plain `net10.0`).
- **`Presentation`** — a Razor Class Library (not an executable): minimal API endpoint definitions, Blazor components, auth filters/middleware. Has **no reference to `Infrastructure`**, not even for DI — that's the point of the split below.
- **`Host`** — the actual executable (`net10.0-windows`, ASP.NET Core + `UseWindowsService()`). The *only* project referencing both `Infrastructure` and `Presentation`; its `Program.cs` is the sole composition root (DI registration, config binding, framework wiring). If you're tempted to add business or presentation logic to `Host`, it belongs in one of the other four projects instead.

**This boundary is mechanically enforced**, not just documented: `tests/OutOfTheBox.ArchitectureTests/LayeringTests.cs` uses NetArchTest to assert it on every `dotnet test` run. A `Domain → Infrastructure` reference (or `Presentation → Infrastructure`, etc.) fails the build, not just a review comment.

**Why `Presentation` can't depend on `Infrastructure`, not even for DI wiring**: this was an explicit, deliberate requirement (see `design.md`'s Architecture section) — `Presentation` and `Infrastructure` must be genuinely independent outer-ring slices with zero connection between them. That's why `Host` exists as a separate project from `Presentation` at all, rather than following the common ASP.NET Core pattern where the Web project's `Program.cs` doubles as both presentation and composition root.

## Conventions

- **Project layout is flat**: each project lives directly at `src/OutOfTheBox.<Name>/` or `tests/OutOfTheBox.<Name>/` — no layer-name or test-type wrapper directory. The `.slnx`'s only solution folder is `Tests` (grouping the three test projects); the five architecture projects sit unfoldered at the solution root. `tests/Fixtures/` is the one exception (see below).
- **Copyright header**: every `.cs` file under `src/` and `tests/OutOfTheBox.{UnitTests,BehaviorTests,ArchitectureTests}` starts with `// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.` followed by a blank line. Do **not** add this to `tests/Fixtures/` (those simulate arbitrary external repos, not this project's own source) or to non-`.cs` files.
- **Public API must have XML doc comments** — enforced by the build (`CS1591` is an error via `Directory.Build.props`), not just convention. `tests/Directory.Build.props` relaxes this one rule for test projects only.
- **Package versions are centrally managed** via `Directory.Packages.props` (CPM) — add new packages there; `.csproj` files reference them with no `Version` attribute.
- **`.editorconfig`** at the repo root covers formatting and C# style conventions, all at `:suggestion` severity — `CS1591` (doc comments) remains the only build-breaking style rule.
- **Build output is centralized** under `artifacts/` at the repo root (`Directory.Build.props`: `UseArtifactsOutput` + an explicit `ArtifactsPath`) — no per-project `bin/`/`obj/`. Applies to `tests/Fixtures/` too. Safe to `rm -rf artifacts` for a clean rebuild.
- **`tests/Fixtures/` is deliberately not in the `.slnx`.** Those are target repos the service spawns real `dotnet` commands against during `BehaviorTests` (`PassingFixture`, `FailingFixture`, `HangingFixture` — a test that never returns, for exercising timeout/cancellation). If they were solution-registered, their failing/hanging tests would break this repo's own `dotnet test` run.
- **Commit after each coherent implementation step**, not one giant commit at the end — e.g. after finishing one `tasks.md` section, not after every file edit.
- **Before every commit, check for leftover temporary code/comments** — debug `Console.WriteLine`/file-based logging, scratch diagnostic instrumentation, commented-out experiments — added while investigating an issue. `git diff --staged` (or `git show --stat`/full diff right after committing) is the reliable way to check; don't rely on memory of what was cleaned up.
- BDD feature files are written alongside the capability they cover (not all upfront) and their Gherkin scenarios are transcribed directly from the corresponding `openspec/.../specs/*/spec.md` `#### Scenario:` blocks, so spec and executable test stay in lockstep.
