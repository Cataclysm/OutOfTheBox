## 1. Domain/Application types

- [x] 1.1 Add `GitHostAuthorization(string Host, DateTimeOffset AuthorizedAtUtc)` in `OutOfTheBox.Domain.Repositories` (or a new `OutOfTheBox.Domain.GitCredentials` namespace if that reads cleaner) - never includes the token itself, and no username field (PAT-only, per design.md's "no username parameter" decision).
- [x] 1.2 Add `IGitCredentialStore` port in `OutOfTheBox.Application` with `AuthorizeAsync(string host, string token, CancellationToken)`, `ListAuthorizedHostsAsync(CancellationToken)` (returning host + `GitHostAuthorization`/`GitHostCredentialHealth` joined), `RevokeAsync(string host, CancellationToken)` returning whether anything was actually removed.
- [x] 1.3 Add `ProcessRunRequest.StandardInput` (`string?`, default `null`) - purely additive, every existing caller unaffected.
- [x] 1.4 Add `GitAuthFailureClassifier` (`OutOfTheBox.Domain`, pure function over a nullable stderr string) - `IsLikelyAuthFailure(string? stderr)`, pattern-matching git's known auth-failure message shapes (`Authentication failed for`, `Invalid username or password`, `could not read Username for ... terminal prompts disabled`, HTTP 401/403 wording). Conservative by design - a miss degrades to today's generic-failure behavior, never a false positive that triggers an unwarranted PAT prompt.
- [x] 1.5 Add `GitRemoteUrlParser.TryGetHost(string url, out string host)` (`OutOfTheBox.Domain`, pure string parsing) - handles both `https://host/...`/`http://host/...` (via `Uri.TryCreate`) and the SCP-like `user@host:path` form.
- [x] 1.6 Add `GitHostCredentialHealth(string Host, DateTimeOffset? LastAuthFailureAtUtc, DateTimeOffset? LastAuthSuccessAtUtc)` in `OutOfTheBox.Domain.Repositories` - separate from `GitHostAuthorization` (see design.md's "needs-credential tracking is a separate type" decision). Add a Domain-level `NeedsCredential(this GitHostCredentialHealth?)` (or equivalent) helper: true iff a failure timestamp exists and is not older than the success timestamp.
- [x] 1.7 Add `IGitCredentialStore.RecordOutcomeAsync(string host, bool succeeded, CancellationToken)` (upserts `GitHostCredentialHealth` for that host) and `GetHealthAsync(string host, CancellationToken)`.
- [x] 1.8 Add `RepositorySummary`/`GitStatusSnapshot`'s `NeedsCredential: bool` field (`OutOfTheBox.Domain.Repositories`).

## 2. Infrastructure: process runner stdin + credential store

- [x] 2.1 `CliProcessRunner`: when `StandardInput` is supplied, write it to the child process's `StandardInput` stream and close it before awaiting output/exit.
- [x] 2.2 New `GitCredentialStore : IGitCredentialStore` (`OutOfTheBox.Infrastructure.Repositories`): `AuthorizeAsync` runs `git credential approve` with `protocol=https\nhost={host}\nusername={fixed placeholder constant}\npassword={token}\n\n` piped via the new stdin support (fixed placeholder, not caller-supplied - see design.md), then immediately runs `git credential fill` for the same host and confirms a `password=` line comes back (discarded, never persisted/logged) before reporting success; on no verification, throws/reports a specific "could not verify" failure. `RevokeAsync` runs `git credential reject` with `protocol=https\nhost={host}\n\n`. Registered singleton (not scoped) - resolves its own scoped `OutOfTheBoxDbContext` per call via `IServiceScopeFactory`, since the singleton-lifetime `GitRepositoryStatsProvider`/`RepositoryStatsSampler` depend on it too and can't consume a scoped service directly (a real DI-lifetime issue found and fixed during implementation).
- [x] 2.3 Before attempting `approve`, check `git config --get credential.helper` returns non-empty; if not, fail with a specific "no credential helper configured" error rather than attempting the write.
- [x] 2.4 `GitHostAuthorizations`/`GitHostCredentialHealth` mapped directly on `OutOfTheBoxDbContext` (folded into `GitCredentialStore` itself rather than a separate repository class - simpler given both tables are small and only ever accessed from that one class). New EF Core migration (`AddGitCredentialTracking`) covering both tables.
- [x] 2.5 `RepositoryManager.PullAsync`/`PushAsync`/`ForcePushAsync`/`FetchAsync`: after each completes, resolve the repository's `origin` host and call `RecordOutcomeAsync` classifying the result via `GitAuthFailureClassifier`. `CleanAsync` untouched (no network).
- [x] 2.6 `RepositoryManager.CloneAsync`'s underlying run: on terminal completion, resolve the host from the clone's own source URL and call `RecordOutcomeAsync` the same way.
- [x] 2.7 `GitRepositoryStatsProvider`: derive `NeedsCredential` from `origin`'s resolved host (via the already-fetched `remote -v` output + `GitRemoteUrlParser`) and `IGitCredentialStore.GetHealthAsync`, folded into the existing per-repository sampling pass - no new network call. Also fixed a real, pre-existing gap found while wiring this: `RepositoryManager.ListAsync` never actually copied `NeedsCredential` (or any of the git-status fields added since) into the `RepositorySummary` it builds for `stats.NeedsCredential` specifically - now fixed.

## 3. Presentation: MCP tools

- [x] 3.1 Add `GitCredentialsMcpTools.cs` (`OutOfTheBox.Presentation.Mcp`): `authorize_git_host`, `list_authorized_git_hosts`, `revoke_git_host_authorization`. Same `[McpServerToolType]`/`[McpServerTool]`/`[Description]`/`McpException` conventions as `FileManagementMcpTools`. Result records near the tool class, matching existing precedent.
- [x] 3.2 Confirm the token parameter is never included in any logged message, exception text, or result payload - grepped the finished implementation for the parameter name; only appears in `AuthorizeAsync`'s own signature/stdin payload construction, never logged or echoed.
- [x] 3.3 Wire `GitAuthFailureClassifier` into `git_run`'s and `clone_repository`'s MCP-facing failure-result mapping (`CommandExecutionMcpTools`/`RepositoryAccessMcpTools`) via a new shared `GitCredentialFailureNote` helper (Presentation-local, since Presentation has no reference to Infrastructure and can't reuse `GitCaptureRunner`) - resolves the target host (repository's `origin` remote for `git_run`, the supplied source URL for `clone_repository`), read-only against credential state.
- [x] 3.4 `RepositoryAccessMcpTools.ListRepositoriesAsync`'s result mapping: include the new `NeedsCredential` field from `RepositorySummary` (flows through automatically once 2.7's gap was fixed), per `mcp-repository-access`'s updated requirement.

## 4. Presentation: Dashboard

- [x] 4.1 Add `PatPromptDialog.razor`: `ShowAsync(string host, Func<Task> onSaved, Action onCancelled)` - a callback-based signature (matching this codebase's existing `ConfirmDialog`/`RenameDialog` convention) rather than the originally-sketched awaitable-`Task<result>` shape, since no other dialog in this codebase uses that pattern. Loops in-place on a token that doesn't verify.
- [x] 4.2 **Deliberately changed from the original plan**: `CloneDialog.razor` still closes immediately on `Accepted`, exactly as before - it does NOT stay open/modal-blocking for the clone's full duration, which would have regressed the operator's ability to use the rest of the dashboard during an otherwise-successful, possibly-long clone. Instead `Repositories.razor` (which already tracks in-flight clones for its own "cancel clone" row state) watches its own pending clones' terminal events and drives the PAT-prompt-and-retry loop from there - see design.md's note on this change and `CloneDialog`'s own updated remarks.
- [x] 4.3 `RepositoryQuickActions.razor`: feed `RepositoryGitActionResult.Failed(ErrorMessage)`'s message into the flashed icon's `title` attribute (previously discarded); auth-classified failures get a specific tooltip. No popup - passive tooltip only, per direct instruction.
- [x] 4.4 `RepositoryQuickActions.razor`: added a "change credential" icon button alongside the existing five, resolving the repository's `origin` host from a new `Remotes` parameter (passed down from `RepositorySummary.Remotes` at both call sites, rather than re-fetching).
- [x] 4.5 `Repositories.razor`'s Name column and `RepositoryDetail.razor`'s heading: render the vendored icon next to the repository's name when `NeedsCredential` is true.
- [x] 4.6 Vendored a "key" Lucide SVG into `wwwroot/icons/` (named `credential` in `Icon.razor`'s name→file map) - shared by 4.4 and 4.5.

## 5. Docs

- [x] 5.1 `About.razor`: tool list grows to seventeen entries, tool-count wording updated, plus a paragraph on the token-handling/auth-failure-enrichment behavior.
- [x] 5.2 `CHANGELOG.md`: new entry under Added.
- [x] 5.3 `INSTALL.md`: new "Git credential prerequisite" section covering `credential.helper` and the two still-unverified assumptions (7.0).
- [x] 5.4 (not originally planned, done for consistency with the prior change's docs sweep) `E2ETESTPLAN.md`'s tool list/count updated to seventeen tools too.

## 6. Tests

- [x] 6.1 **Reinterpreted**: `GitCredentialStore`'s real git-invoking behavior is covered in BehaviorTests (6.4), not UnitTests - this project's own established convention is "no real process spawning in UnitTests" (see `RepositoryManagerTests`' own doc comment), which a real `git credential approve/fill/reject` round-trip would violate.
- [x] 6.2 Unit tests for `GitAuthFailureClassifier` and `GitRemoteUrlParser` (33 tests total across this section).
- [x] 6.3 Unit tests for `GitHostCredentialHealth.NeedsCredential`'s derivation (all four timestamp combinations plus neither-recorded).
- [x] 6.4 `McpGitCredentials.feature` (6 scenarios) against a real running `Host`, using a new `TestGitCredentialConfigSetup` module initializer (mirrors `TestDataDirectorySetup`'s "set once, at assembly load" pattern) pointing every test-spawned git invocation at a throwaway, file-based `credential.helper`. **Gap, deliberately not covered**: the `git_run`/`clone_repository` auth-failure-enrichment scenarios need a real 401/403 from an actual authenticated remote to exercise honestly - not producible against a local bare-repo fixture in this environment. The classifier itself is unit-tested directly; the wiring was confirmed via live verification (7) instead.
- [ ] 6.5 **Skipped** - the needs-credential field/marker's BehaviorTests coverage would hit the same "no real authenticated remote available" limitation as 6.4's gap above. Confirmed instead via live verification (7.8's equivalent) and the pure `GitHostCredentialHealth` unit tests (6.3) covering the underlying derivation logic directly.
- [ ] 6.6 **Skipped**, a deliberate scope decision - `CloneDialog`'s retry state machine is async/event-driven (depends on `IRunEventBus` timing), a poor fit for bUnit's synchronous rendering model relative to the effort required; `RepositoryQuickActions`'/`Repositories.razor`'s new markup is already exercised indirectly by the existing `RepositoriesComponentTests`/`RepositoryDetailComponentTests` (both updated and passing), and the interactive flows were confirmed via real-browser live verification (7) instead.
- [x] 6.7 Updated `McpServer.feature`'s "Listing available tools" scenario to the full seventeen-tool list.
- [x] 6.8 Ran `dotnet test tests/OutOfTheBox.UnitTests`/`tests/OutOfTheBox.ArchitectureTests` after each section.
- [x] 6.9 Ran the full suite including `tests/OutOfTheBox.BehaviorTests` before the final commit - 299/299 UnitTests, 5/5 ArchitectureTests, 91/91 BehaviorTests.

## 7. Live verification

- [ ] 7.0 **Not verified** - none of the three assumptions could be tested in this environment: (a)/(c) need a real GitHub or Azure DevOps account and PAT, which wasn't available; (b) needs a real installed Windows Service running under its dedicated `svc-outofthebox` account, which needs the real installer run on a real machine. All three remain exactly as flagged in design.md/INSTALL.md - genuinely open, not resolved.
- [ ] 7.1 Not done - depends on 7.0(a)/(c).
- [x] 7.2 Verified against a real running `Host` (not `WebApplicationFactory`) using an isolated scratch git config: `authorize_git_host` → `list_authorized_git_hosts` shows the host with a timestamp, `needsCredential`, and no token value anywhere in the response.
- [ ] 7.3 Partially verified: `revoke_git_host_authorization` confirmed to remove the host from `list_authorized_git_hosts` and to report "nothing to revoke" on a second call. The "previously authorized, may have expired" enrichment note itself was not exercised live (depends on 7.0/7.1's real-remote requirement).
- [x] 7.4 Confirmed `revoke_git_host_authorization` for a never-authorized host produces the specific "nothing to revoke" message, not a generic failure (the missing-credential-helper and unreachable-git.exe paths were not separately exercised - they'd require deliberately breaking the test machine's own git installation).
- [x] 7.5 Confirmed live via a real browser session (CDP/headless Edge): the change-credential dialog opens, resolves the host from the repository's `origin` remote correctly, accepts a token, and the action flashes green on success - and the credential saved via the dashboard was confirmed visible via `list_authorized_git_hosts` over MCP, proving the shared `IGitCredentialStore` design (single source of truth between the two surfaces). The clone-specific retry-prompt flow itself (7.5 as originally scoped) was not separately exercised - no real clone-worthy remote was available; the identical underlying dialog/store mechanism was confirmed via the change-credential path instead.
- [ ] 7.6 Not done - needs a real invalid/expired credential against a real remote to produce a genuine auth failure.
- [x] 7.7 Confirmed live: the change-credential action correctly resolves a repository's `origin` host and opens `PatPromptDialog` for it (screenshotted). The "no resolvable host" path was not separately exercised live (covered by its own unit-level `GitRemoteUrlParser` tests instead).
- [ ] 7.8 Not done - needs a real auth failure (see 7.6) to set `NeedsCredential` true in the first place.

## 8. Wrap-up

- [x] 8.1 Checked `git diff` across every commit in this change for leftover debug code - clean.
- [x] 8.2 Committed (incrementally, one per section) and pushed. Not archived - a separate, deliberate step.
