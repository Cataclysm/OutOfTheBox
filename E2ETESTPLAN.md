# End-to-End Test Plan

**Status: planned, not yet executed.** This document describes a full, sandbox-realistic workflow
test against a real deployed instance of OutOfTheBox — the same shape of session a Claude Code
instance running in an sbx sandbox would actually drive, calling the MCP server directly (there is
no client-side skill/doc to hand it - every tool is self-describing, see `README.md`'s "Running a
Claude Code instance in the sbx sandbox"). It is deliberately **not** run by the session that wrote
it: the operator will install the latest build first, then run this plan themselves (or hand it to a
fresh sbx-side agent, configured per that README section, with only this document as context).

This plan supersedes nothing in `tasks.md` §19 (End-to-End Verification) — it's a superset, written
as an actually-runnable script/checklist rather than a list of things to eyeball, and it specifically
exercises everything added by `tasks.md` §22 (live-update fix, two-cadence stats, remotes/gone-branch,
pull/push/force-push/fetch/clean, clone branch selection, branch-switch, icon/dialog UI).

## Scope split: what an sbx agent can automate vs. what needs a human + browser

This matters because half of what §22 added is **dashboard-only, with no MCP tool** (per direct
instruction — pull/push/force-push/fetch/clean and branch-switch are unreachable to an sbx caller
by design). An automated agent following only the MCP tools cannot exercise those directly; it can
only exercise their *effects* indirectly (e.g. running `git push` itself via `git_run` and
confirming the dashboard reflects it), or by treating the equivalent generic `git` subcommand as a
stand-in. Repository and file/directory deletion were dashboard-only at the time this plan was
written, but that decision has since been reversed — `delete_repository` and `delete_path` are now
MCP-reachable too (see `openspec/changes/expose-file-management-mcp/`) and belong with Phases 1–7,
not Phase 8; this plan hasn't been re-run against that change yet.

- **Phases 1–7**: fully automatable by an sbx agent using only the MCP tools (`dotnet_run`,
  `git_run`, `read_run_output`, `cancel_run`, `transfer_file`, `list_repositories`,
  `clone_repository`, `delete_repository`, `find_files`, `get_file_info`, `delete_path`,
  `authorize_git_host`, `list_authorized_git_hosts`, `revoke_git_host_authorization`,
  `authorize_nuget_feed`, `list_authorized_nuget_feeds`, `revoke_nuget_feed_authorization`) over the
  bearer-authenticated `/mcp` endpoint — no browser needed. This plan predates
  `openspec/changes/expose-git-credentials-mcp/` and `openspec/changes/expose-nuget-credentials-mcp/`
  too and hasn't been re-run against either.
- **Phase 8**: requires a human operator with a browser (or a browser-automation tool the agent has
  been explicitly given). Listed separately so an sbx-only run can still produce a complete report for
  Phases 1–7 and flag Phase 8 as "not executed in this environment" rather than silently skipping it.

## Prerequisites

1. Latest build installed and the service running (Windows Service or `dotnet run --project
   src/OutOfTheBox.Host`), reachable at its configured HTTPS endpoint.
2. A valid bearer token for the instance under test.
3. `OutOfTheBox:RootDirectory` writable by the service account, with room for a throwaway test
   repository (a few MB).
4. A git-reachable clone source reachable *from the host*, one of:
   - a real public repository (e.g. a small, disposable GitHub repo the operator controls), or
   - a local bare repository created on the host itself, e.g.:
     ```
     git init --bare C:\temp\e2e-remote.git
     git clone C:\temp\e2e-remote.git C:\temp\e2e-seed
     cd C:\temp\e2e-seed
     git commit --allow-empty -m "seed"
     git checkout -b feature/e2e
     git commit --allow-empty -m "feature commit"
     git push origin main feature/e2e
     ```
     (the same shape `tests/OutOfTheBox.BehaviorTests/Support/GitFixture.cs` builds programmatically
     for BDD scenarios — reuse that pattern if scripting this instead of doing it by hand). Using a
     **local** bare remote (rather than only GitHub) is what makes Phase 3's push/pull/fetch steps
     possible without depending on external network access or write credentials to a public host.
5. A recording mechanism for the report (see "Report format" below) — timestamps, tool call
   arguments/results, and pass/fail per step.

## Phase 1 — Connectivity and authentication

1.1. Open a Streamable HTTP MCP session against `https://<host>:<port>/mcp` with no `Authorization`
     header (or a wrong bearer token) → expect the connection/request to be rejected (`401`).
1.2. Same, with the correct `Authorization: Bearer <token>` header → expect the session to establish
     and tool discovery (the client library's own initialize/list-tools handshake) to succeed, listing
     all twenty tools (`dotnet_run`, `git_run`, `read_run_output`, `cancel_run`, `transfer_file`,
     `list_repositories`, `clone_repository`, `delete_repository`, `find_files`, `get_file_info`,
     `delete_path`, `authorize_git_host`, `list_authorized_git_hosts`,
     `revoke_git_host_authorization`, `authorize_nuget_feed`, `list_authorized_nuget_feeds`,
     `revoke_nuget_feed_authorization`, `get_run_resources`, `get_environment_info`,
     `get_file_lock_info`).
1.3. Call `list_repositories` with no arguments → expect a well-formed (possibly empty) result array.

**Pass criteria**: 1.1 rejects, 1.2/1.3 succeed.

## Phase 2 — Repository clone, with an explicit branch

2.1. Call `clone_repository` with `{ "url": "<seed remote>", "name": "e2e-test-repo", "branch":
     "feature/e2e" }`. Record the returned `runId`, then poll `read_run_output` (starting at offset 0)
     until its `status` reaches a terminal state - confirm it's `completed`, not `failed`/`timedOut`.
2.2. Poll `list_repositories` until `e2e-test-repo` appears with `statsComputed: true` (bounded wait —
     fail if not observed within `DefaultExecutionTimeoutSeconds`).
2.3. Confirm the cloned repository's reported `branch` is `feature/e2e`, **not** the remote's default
     branch — this is the direct test of §22.12's `--branch` clone support.
2.4. Attempt a second `clone_repository` into the same name → expect the call to be rejected
     (`McpException`, "already exists"), and confirm nothing about the first clone changed.
2.5. Attempt a clone with a name that would resolve outside the configured root (e.g. `../escape`) →
     expect a validation rejection, and confirm no directory was created.

**Pass criteria**: 2.1–2.3 succeed and the branch matches; 2.4/2.5 are both cleanly rejected.

## Phase 3 — Git operations via `git_run`

All of these target `e2e-test-repo` and use the start-then-poll contract every `dotnet_run`/`git_run`
call shares: the tool call itself returns `{ runId, status: "running" }` immediately, then
`read_run_output(runId, offset)` is polled (0 for the first call, each result's `nextOffset` after
that) until `status` reaches a terminal state, at which point `stdout`/`stderr`/`exitCode` are
complete.

3.1. `git_run` with `{ "arguments": ["status", "--porcelain"], "workingDirectory": "e2e-test-repo" }`
     → expect a clean tree (empty stdout) immediately after clone.
3.2. Modify a tracked file (e.g. via a `dotnet` file-producing step, or by having the agent write to
     a file inside the repo through its own sandbox tooling if the repo is otherwise shared — if the
     agent has no direct filesystem access to the host, substitute `git commit --allow-empty -m
     "e2e change"` to produce a divergent commit instead) → re-run 3.1's `git status --porcelain` →
     expect a dirty/ahead result.
3.3. `git_run` with `["add", "-A"]` then `["commit", "-m", "e2e change"]` (two separate calls, or one
     `["commit", "-am", "e2e change"]` if nothing new needs staging).
3.4. `git_run` with `["push"]` → confirm it succeeds against the local bare remote from the
     Prerequisites step.
3.5. `git_run` with `["fetch"]` then `["status", "--porcelain", "-b"]` (or `["rev-list",
     "--left-right", "--count", "@{upstream}...HEAD"]`) → confirm ahead/behind now reads `0`/`0`
     post-push.
3.6. `git_run` with `["branch", "-r"]` → confirm `origin/main` and `origin/feature/e2e` are both
     visible.
3.7. `git_run` with `["checkout", "main"]` then `["checkout", "feature/e2e"]` → confirm both succeed
     (exercises the same checkout mechanics §22.15's dashboard branch-switch uses internally, via the
     generic passthrough rather than the dashboard-only action).
3.8. `git_run` with `["clean", "-ndf"]` (dry-run, **not** `-xdf`) → confirm it reports what *would* be
     removed without an sbx-triggered agent ever running the real destructive `clean -xdf` (that stays
     dashboard-only by design — see Phase 8.6).

**Pass criteria**: every step's `read_run_output` eventually reaches `status: "completed"` with exit
code 0 except where a non-zero exit is the expected outcome (none in this phase); 3.5's ahead/behind
matches expectations after push.

## Phase 4 — `dotnet build` / `dotnet test`

Run these against `e2e-test-repo` if it's a .NET project, or clone one of this repository's own
`tests/Fixtures/*` shapes into the test root instead (`PassingFixture` for a clean pass,
`FailingFixture` to confirm failure is reported correctly, **not** `HangingFixture` here — that one's
deliberately for the cancellation test in Phase 6, not this phase).

4.1. `dotnet_run` with `{ "arguments": ["build"], "workingDirectory": "<repo>" }` → poll
     `read_run_output` to completion → expect success (or the fixture's known outcome) with full
     stdout/stderr captured.
4.2. `dotnet_run` with `{ "arguments": ["test"], "workingDirectory": "<repo>" }` → same.
4.3. Confirm both runs show up in the dashboard's History, keyed by the `runId`s recorded above
     (cross-referenced with Phase 8, but the two `runId`s to look for are recorded here). If the build
     failed unexpectedly, call `get_environment_info` and/or `get_file_lock_info` (for a suspicious
     output file) to rule out an environment/file-lock cause before treating it as a real regression.
4.4. Confirm `get_run_resources(runId)` for one of the two runs (called while it's still running, or
     immediately after) returns a non-empty CPU/RAM sample series rather than an empty result.

**Pass criteria**: exit codes and stdout/stderr match what running the same commands locally against
the same fixture would produce; 4.4 returns real samples.

## Phase 5 — File transfer

5.1. After Phase 4.1 produces build output, `transfer_file` with `{ "repository": "<repo>", "path":
     "<path to a known output file, e.g. a .dll under bin/>" }` → base64-decode `contentBase64` and
     confirm the bytes match the file on disk exactly (byte-for-byte comparison, not just a size
     check).
5.2. Attempt a path-escape request (e.g. `path: "../../Windows/System32/some.dll"`) → expect the call
     to be rejected without any bytes transferred.

**Pass criteria**: 5.1's bytes match exactly; 5.2 is cleanly rejected with no partial transfer.

## Phase 6 — Cancellation

6.1. Clone (or reuse) a repository containing `HangingFixture`'s shape (`Task.Delay(Timeout.Infinite)`)
     and start `dotnet_run` with `["test"]` against it.
6.2. Confirm the run is visible as in-flight (`read_run_output` reports `status: "running"`), then
     call `cancel_run(runId)`.
6.3. Poll `read_run_output` until `status` reaches `cancelled`, and confirm the repository's lock is
     released immediately after (a subsequent unrelated `git_run` against the same repository should
     succeed right away, not be rejected as busy).

**Pass criteria**: cancellation is prompt (bounded wait, not "eventually"), and the lock release is
verified by a follow-up command succeeding.

## Phase 7 — Stats freshness (the actual bug this round of work fixed)

This phase specifically targets the root-caused sampler crash (`tasks.md` 22.1) and the two-cadence
split (22.2) — the original complaint that motivated this whole change.

7.1. Immediately after Phase 3's push/commit activity, poll `list_repositories` at a short interval
     (e.g. every 2s) and record the wall-clock time at which `e2e-test-repo`'s reported `branch`/
     `isDirty`/`aheadCount`/`behindCount` fields change to reflect the new state. This should happen
     within roughly one `RepositoryGitStatusIntervalSeconds` (default 10s) of the underlying change,
     **not** require another run to complete first and **not** simply never update.
7.2. Separately, note the wall-clock time at which `totalSizeBytes` next changes after a change that
     actually altered on-disk size (e.g. a `dotnet build` producing new files) — this should land
     within roughly one `RepositoryStatsSamplerIntervalSeconds` (default 60s), materially slower than
     7.1's git-status update, confirming the two cadences are actually decoupled and not just one
     interval renamed.
7.3. Leave the service running for at least 2× the size interval with no activity against any
     repository, and confirm the service is still responsive (`list_repositories` still succeeds) —
     this is the direct regression test for the crash-the-whole-host bug: previously, a single git
     invocation failure anywhere in the sweep could silently kill the entire host, and the *next* call
     here would simply fail to connect at all.

**Pass criteria**: 7.1 updates within ~1 fast interval; 7.2 updates within ~1 slow interval and is
visibly slower than 7.1; 7.3 shows the service still alive and responsive throughout.

## Phase 8 — Dashboard-only manual verification (human + browser required)

Everything below has no MCP tool and must be checked visually in a real browser session
against the live dashboard. Each item references the `tasks.md` §22 task it verifies.

8.1. **Icons, not text** (22.19): open the Repositories view; confirm clone/delete/pull/push/
     force-push/fetch/clean/clear-filters are all icon buttons, styled consistently with the rest of
     the dark theme, each icon recognizable for its action (trash can = delete, etc.).
8.2. **Clone popup dialog** (22.17): click the clone icon; confirm a popup dialog opens (not an
     inline reveal below the toolbar); enter a source URL and confirm the branch dropdown populates
     shortly after (22.13/22.14); complete a clone through the dialog and confirm it closes and the
     new repository appears in the list.
8.3. **Delete confirmation popup** (22.16): click delete on a test repository; confirm a popup asks
     for confirmation (the button itself does **not** change label/state in place); cancel once to
     confirm nothing happens, then confirm to verify it actually deletes.
8.4. **Pull/push/fetch — icon flash, no console** (22.9): trigger each from the list; confirm no
     output/console view opens, and the icon itself briefly turns green (success) or red (failure)
     for a few seconds, then reverts.
8.5. **Force-push and clean require confirmation** (22.10): trigger each; confirm the same popup
     confirmation flow as delete appears before anything runs.
8.6. **`git clean -xdf` actually removes untracked files**: create an untracked file in a test
     repository via any means, trigger Clean from the dashboard, confirm the file is gone and the
     repository's git status reflects a clean tree. This is the one live-deletion effect Phase 3
     deliberately avoided (it only ran `clean -ndf`, a dry run) precisely because it's dashboard-only
     and irreversible.
8.7. **Repository detail: clone source and remotes** (22.5–22.7): open the test repository's detail
     page; confirm its clone source URL and full remotes list are shown and match what was actually
     used/configured.
8.8. **Branch-switch dropdown, with auto-tracking** (22.15): on the same detail page, confirm the
     branch dropdown lists both `main` and `feature/e2e`; switch to a remote branch that has no local
     counterpart yet (create one on the bare remote first if none exists) and confirm it's checked
     out and subsequently listed as local, not remote, on next view.
8.9. **Remote-gone indication** (22.4): delete a remote branch directly on the bare remote
     (`git push origin --delete feature/e2e` from a separate clone, not through the dashboard) while
     the test repository still tracks it; confirm the dashboard's git status shows "remote gone"
     rather than silently reporting no ahead/behind the same way "no upstream" would.
8.10. **Title underline** (22.20): visually confirm the underline beneath "Repositories"/"History"/
      "Status"/a repository-detail heading spans exactly the heading text's width, the same way the
      top navigation's underline already does, on at least two headings of different lengths.
8.11. **Version display**: confirm the dashboard's About page shows a version number and that it
      matches the build actually installed (e.g. the installer's own displayed version at install
      time, or `artifacts/publish/OutOfTheBox.Host/OutOfTheBox.Host.exe`'s file version).

**Pass criteria**: every sub-item visually confirmed exactly as described; note any visual glitch,
misalignment, or icon that doesn't match its action even if functionally correct.

## Cleanup

- Delete `e2e-test-repo` (and any other repositories created during the run) via the dashboard, and
  confirm via `list_repositories` that it's gone.
- Remove the local bare remote and seed clone from the Prerequisites step (`C:\temp\e2e-remote.git`,
  `C:\temp\e2e-seed`) if they were created solely for this run.
- Confirm no stray `git`/`dotnet` processes remain (Status view's process list, or Task Manager).

## Report format

The completed run should produce a single report containing, per phase:

- Pass/fail per numbered step (not just per phase).
- For MCP steps: tool call sent (name + arguments), result received, and timing.
- For Phase 7: the actual measured wall-clock delay for each freshness check, not just "it worked."
- For Phase 8: a one-line note per item (confirmed / not confirmed / discrepancy found), since these
  can't carry a machine-checkable transcript the way Phases 1–7 can.
- A summary section: total steps, pass count, fail count, and a short list of any deviations from
  expected behavior, each tied back to the `tasks.md` §22 item it relates to.
