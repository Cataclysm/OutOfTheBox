# mcp-environment-info Specification

## Purpose
Lets an MCP caller inspect this host's installed .NET/git toolchain, configured NuGet sources, and available disk space, to diagnose a restore or build failure caused by an environment mismatch (missing SDK/workload, an unreachable or misconfigured feed, no disk space left) rather than a code problem.
## Requirements
### Requirement: Caller can retrieve the host's installed toolchain and environment state
The system SHALL accept a `get_environment_info` tool call (no parameters) and SHALL return: the installed `dotnet` and `git` versions; every installed .NET SDK (version and install path); configured NuGet package sources (name, URL, enabled/disabled state); and available/total disk space on the drive containing the configured root directory.

#### Scenario: Retrieving environment info
- **WHEN** an authenticated caller calls `get_environment_info`
- **THEN** the result includes the host's actual installed `dotnet`/`git` versions, its actual installed SDK list, its actual configured NuGet sources, and plausible disk space figures for the configured root directory's drive

### Requirement: Installed workloads are reported best-effort, never failing the call
The system SHALL attempt to report the host's installed .NET workloads as part of `get_environment_info`, but a failure to run or parse that specific probe SHALL NOT cause the tool call itself to fail or omit its other fields - an empty workload list SHALL be returned instead.

#### Scenario: Workload probe unavailable or unparseable
- **WHEN** an authenticated caller calls `get_environment_info` on a host where the workload listing command fails, is unavailable, or returns output this service cannot parse
- **THEN** the call still succeeds, returning every other field normally with an empty workload list

### Requirement: The reported dotnet/git versions match the dashboard's own
The system SHALL report the same `dotnet`/`git` version values `get_environment_info` returns as the dashboard's Status page already displays, from the same underlying source, rather than probing them a second time independently.

#### Scenario: Consistency with the dashboard
- **WHEN** the dashboard's Status page shows a given `dotnet` and `git` version
- **THEN** a `get_environment_info` call made around the same time reports the identical versions

