# Building and Testing

## Prerequisites

- Windows (the service itself, and several of its tests, use Windows-only APIs — `PerformanceCounter`, WMI, `GlobalMemoryStatusEx`, Windows Service hosting)
- .NET 10 SDK

No other tooling is required. `.slnx` opens directly in Rider; no Visual Studio dependency anywhere in the toolchain (Reqnroll's BDD tests run via plain `dotnet test`, same as everything else).

## Build

```
dotnet build OutOfTheBox.slnx
```

## Test

The full suite, in one command:

```
dotnet test OutOfTheBox.slnx
```

This runs three test projects with very different speed characteristics. During day-to-day development it's usually faster to run them separately:

```
# Fast: pure logic, no process spawning, no real I/O beyond a couple of temp-directory tests
dotnet test tests/OutOfTheBox.UnitTests/OutOfTheBox.UnitTests.csproj

# Fast: reflection over the built assemblies, no execution of "real" code
dotnet test tests/OutOfTheBox.ArchitectureTests/OutOfTheBox.ArchitectureTests.csproj

# Slow (several seconds to tens of seconds): genuinely spawns dotnet.exe against the checked-in
# fixture repos under tests/Fixtures/, including two scenarios that deliberately let a command
# hang and confirms it gets killed by its timeout.
dotnet test tests/OutOfTheBox.BehaviorTests/OutOfTheBox.BehaviorTests.csproj
```

## Test project layout

| Project | Framework | What it covers |
|---|---|---|
| `UnitTests` | xUnit | Isolated `Domain`/`Application` logic, plus targeted `Infrastructure` tests (e.g. real filesystem/temp-directory behavior) that don't need a live process or network |
| `BehaviorTests` | Reqnroll (Gherkin) + xUnit | End-to-end scenarios through the real ASP.NET Core pipeline (`WebApplicationFactory<Program>`), including real `dotnet.exe` child processes against `tests/Fixtures/` |
| `ArchitectureTests` | xUnit + NetArchTest | Mechanically enforces the Clean Architecture dependency rules from `design.md` |

`tests/Fixtures/` (`PassingFixture`, `FailingFixture`, `HangingFixture`, `GitFixture`) are deliberately **not** part of the `.slnx` — they're target repos the service spawns `dotnet`/`git` against during `BehaviorTests`, not test projects of this repo. Running `dotnet test` on the solution never touches them directly for that reason (a failing/hanging test inside one would otherwise break this repo's own test run).

`installer/OutOfTheBox.Msi.CustomActions.Tests` (xUnit, net472) is a separate, standalone test suite covering the installer's own custom-action logic (bearer token/service-account-password generation and precedence). It's outside `OutOfTheBox.slnx` and this doc's scope — see [`INSTALL.md`](INSTALL.md) for building the installer itself; run it directly with `dotnet test installer/OutOfTheBox.Msi.CustomActions.Tests`.

## Code quality gates

- `Directory.Build.props` enables `<Nullable>enable</Nullable>` and `<GenerateDocumentationFile>true</GenerateDocumentationFile>` with the missing-XML-doc-comment warning (`CS1591`) promoted to an error — a public type or member without a `///` doc comment fails the build. `tests/Directory.Build.props` relaxes this one rule for test projects (xUnit requires public test classes/methods, which aren't "public API" in the sense this rule targets).
- Every `.cs` file under `src/` and `tests/{UnitTests,BehaviorTests,ArchitectureTests}` (not `tests/Fixtures/`) starts with a standard copyright header — see `CLAUDE.md` for the exact text and where it applies.
- `.editorconfig` at the repo root covers formatting and modern-C#-construct style conventions (pattern matching, target-typed `new`, collection expressions, unused usings/parameters, ...), enforced at `warning` severity and surfaced during `dotnet build` itself (`EnforceCodeStyleInBuild`), not just as IDE-level hints — still non-build-breaking, since `CS1591` above remains the only rule promoted to an error.
- `Directory.Packages.props` centralizes every third-party package version — add new packages there, then reference them from a project's `.csproj` with no `Version` attribute.

## Project layout

`src/` and `tests/` are both flat: each project sits directly under `src/OutOfTheBox.<Name>/` or `tests/OutOfTheBox.<Name>/`, with no extra layer-name or test-type wrapper directory. The `.slnx`'s only solution folder is `Tests` (grouping the three test projects for IDE display); the five architecture projects sit unfoldered at the solution root. `tests/Fixtures/` is the one exception — see above.

## Build output

Every project (including `tests/Fixtures/`, even though it isn't solution-registered) builds into a single `artifacts/` directory at the repo root instead of a `bin/`/`obj/` pair inside each project folder — the .NET SDK's centralized artifacts output layout (`Directory.Build.props`: `UseArtifactsOutput` + an explicit `ArtifactsPath`, needed because `tests/Directory.Build.props` would otherwise anchor test-project output to `tests/artifacts/` instead of the repo root). Output lands at `artifacts/bin/<ProjectName>/debug/` and `artifacts/obj/<ProjectName>/`; `artifacts/` is gitignored. Delete it freely (`rm -rf artifacts`) to force a clean rebuild — nothing under it is ever hand-edited.
