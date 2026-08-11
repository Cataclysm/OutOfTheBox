## Why

Ahead/behind counts and "remote branch gone" detection are computed purely from already-known local refs (`RepositoryStatsSampler`'s fast git-status cadence never touches the network) - they only ever reflect what the last real `git fetch`/`pull`/`push` actually saw. Today that only happens when an operator manually triggers pull/push/fetch from the dashboard, or an MCP `git_run`/`dotnet_run` caller happens to touch that repository. A repository nobody touches shows stale ahead/behind indefinitely, even though nothing prevents this service from just fetching it itself. Per direct instruction: fetch every repository in the background regularly, on a much longer interval than the existing (purely local) stats cadences - 5 minutes.

## What Changes

- Add a new background sampler (`RepositoryFetchSampler`) that runs `git fetch` against every repository under the configured root on its own cadence (default 300s, configurable via `RepositoryFetchIntervalSeconds`), independent of and much slower than `RepositoryStatsSampler`'s existing git-status (10s)/size (60s) cadences - those stay purely local and unchanged.
- Goes through the existing `IRepositoryManager.FetchAsync` (the same method the dashboard's per-repository Fetch action already calls), not a raw `git fetch` invocation - so every existing side effect of a fetch applies identically here: the per-repository lock (a repository already busy with another run is skipped for that tick, not queued or retried early), credential-outcome recording (a repository nobody has manually touched now also self-detects, and self-clears, a broken credential via this same sweep - see `RepositoryCredentialHealth`), and the immediate stats refresh/publish on completion.
- One repository's fetch failing (or throwing unexpectedly) never stops the sweep for the rest, nor the sampler's own `BackgroundService`, matching `RepositoryStatsSampler`'s and `HostResourceSamplerService`'s existing crash-resilience pattern for the identical class of risk.

## Capabilities

### Modified Capabilities
- `repository-management`: adds a new requirement that every repository is fetched automatically in the background, independent of any operator/MCP-triggered action.

## Impact

- **Affected code**: one new file, `src/OutOfTheBox.Infrastructure/Repositories/RepositoryFetchSampler.cs` (new `BackgroundService`, registered alongside `RepositoryStatsSampler` in `RepositoryManagementServiceCollectionExtensions`), a new `ServiceOptions.RepositoryFetchIntervalSeconds` (default 300), and its `appsettings.json` entry.
- **No new dependencies, no schema/migration changes.** Reuses `IRepositoryManager`/`IGitCredentialStore`/`RepositoryCredentialHealth` exactly as they already exist.
- **No REST/MCP-surface change**: this is purely a background side effect of the existing repository set - `list_repositories`/the dashboard already reflect whatever `FetchAsync` produces, the same way they already do for an operator-triggered fetch.
