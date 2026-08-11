## 1. Configuration

- [x] 1.1 Add `ServiceOptions.RepositoryFetchIntervalSeconds` (default 300), documented alongside the existing `RepositoryGitStatusIntervalSeconds`/`RepositoryStatsSamplerIntervalSeconds` cadence fields.
- [x] 1.2 Add the corresponding `appsettings.json` entry (explicit, matching the existing interval fields' own convention of being listed even at their default value).

## 2. Infrastructure: the sampler

- [x] 2.1 Add `RepositoryFetchSampler : BackgroundService` (`OutOfTheBox.Infrastructure.Repositories`) - a single `PeriodicTimer` loop at `RepositoryFetchIntervalSeconds`, calling a public `FetchAllOnceAsync` (exposed the same way `RepositoryStatsSampler.RecomputeAllOnceAsync` is, for direct test coverage without the real timer).
- [x] 2.2 `FetchAllOnceAsync`: resolve a fresh `IServiceScopeFactory`-created scope's `IRepositoryManager` (mirroring `GitCredentialStore`'s own captive-scoped-dependency pattern), call `ListAsync`, and run `FetchAsync` sequentially for every summary with `IsGitRepository == true`.
- [x] 2.3 Guard both the enumeration and each individual `FetchAsync` call against an unexpected exception (log and continue) - the same crash-resilience requirement `RepositoryStatsSampler.GuardAsync` documents. A `Rejected` result (busy/not found) is not logged (routine); a `Failed` result is logged at Warning.
- [x] 2.4 Register `RepositoryFetchSampler` as a hosted service in `RepositoryManagementServiceCollectionExtensions`, alongside `RepositoryStatsSampler`.

## 3. Drive-by fix found while touching this area

- [x] 3.1 `RepositorySummary.NeedsCredential`'s doc comment still described the pre-fix host-scoped `GitHostCredentialHealth` derivation, left stale by the earlier per-repository credential-health fix - corrected to reference `RepositoryCredentialHealth`.

## 4. Tests

- [x] 4.1 Unit tests for `RepositoryFetchSampler.FetchAllOnceAsync` against a fake `IRepositoryManager`: a non-git summary is never fetched; one repository's `FetchAsync` throwing does not stop the sweep for the rest; a `Failed` result does not throw. Mirrors `RepositoryStatsSamplerTests`' own pattern.
- [x] 4.2 Full `dotnet test` (UnitTests, ArchitectureTests, BehaviorTests) run clean with the new hosted service registered - confirms it composes correctly at startup (`WebApplicationFactory`-hosted `BehaviorTests` already exercise full DI composition) without needing a live-fetch-specific scenario (no real authenticated remote available in this environment, the same limitation already accepted for the credential-health change's own equivalent gaps).

## 5. Docs

- [x] 5.1 `repository-management` spec delta: new requirement describing the background fetch cadence, its independence from the existing two (local) cadences, and that a busy repository is skipped rather than queued.
- [x] 5.2 `CHANGELOG.md` entry under `Added`.
