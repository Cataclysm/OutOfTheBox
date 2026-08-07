# Building and Testing

## Prerequisites

- Windows (the service itself, and several of its tests, use Windows-only APIs — `PerformanceCounter`, WMI, `GlobalMemoryStatusEx`, Windows Service hosting)
- .NET 10 SDK

No other tooling is required. `.slnx` opens directly in Rider; no Visual Studio dependency anywhere in the toolchain (Reqnroll's BDD tests run via plain `dotnet test`, same as everything else).

## Build

```
dotnet build BuildAndTestService.slnx
```

## Test

The full suite, in one command:

```
dotnet test BuildAndTestService.slnx
```

This runs three test projects with very different speed characteristics. During day-to-day development it's usually faster to run them separately:

```
# Fast: pure logic, no process spawning, no real I/O beyond a couple of temp-directory tests
dotnet test tests/UnitTests/BuildAndTestService.UnitTests/BuildAndTestService.UnitTests.csproj

# Fast: reflection over the built assemblies, no execution of "real" code
dotnet test tests/ArchitectureTests/BuildAndTestService.ArchitectureTests/BuildAndTestService.ArchitectureTests.csproj

# Slow (several seconds to tens of seconds): genuinely spawns dotnet.exe against the checked-in
# fixture repos under tests/Fixtures/, including two scenarios that deliberately let a command
# hang and confirms it gets killed by its timeout.
dotnet test tests/BehaviorTests/BuildAndTestService.BehaviorTests/BuildAndTestService.BehaviorTests.csproj
```

## Test project layout

| Project | Framework | What it covers |
|---|---|---|
| `UnitTests` | xUnit | Isolated `Domain`/`Application` logic, plus targeted `Infrastructure` tests (e.g. real filesystem/temp-directory behavior) that don't need a live process or network |
| `BehaviorTests` | Reqnroll (Gherkin) + xUnit | End-to-end scenarios through the real ASP.NET Core pipeline (`WebApplicationFactory<Program>`), including real `dotnet.exe` child processes against `tests/Fixtures/` |
| `ArchitectureTests` | xUnit + NetArchTest | Mechanically enforces the Clean Architecture dependency rules from `design.md` |

`tests/Fixtures/` (`PassingFixture`, `FailingFixture`, `HangingFixture`) are deliberately **not** part of the `.slnx` — they're target repos the service spawns `dotnet` against during `BehaviorTests`, not test projects of this repo. Running `dotnet test` on the solution never touches them directly for that reason (a failing/hanging test inside one would otherwise break this repo's own test run).

## Code quality gates

- `Directory.Build.props` enables `<Nullable>enable</Nullable>` and `<GenerateDocumentationFile>true</GenerateDocumentationFile>` with the missing-XML-doc-comment warning (`CS1591`) promoted to an error — a public type or member without a `///` doc comment fails the build. `tests/Directory.Build.props` relaxes this one rule for test projects (xUnit requires public test classes/methods, which aren't "public API" in the sense this rule targets).
- Every `.cs` file under `src/` and `tests/{UnitTests,BehaviorTests,ArchitectureTests}` (not `tests/Fixtures/`) starts with a standard copyright header — see `CLAUDE.md` for the exact text and where it applies.
