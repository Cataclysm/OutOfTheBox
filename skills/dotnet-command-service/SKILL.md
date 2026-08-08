---
name: dotnet-command-service
description: Client guide for calling the Out of the Box service from a sandboxed Claude Code instance - run dotnet/git commands, download files, and list/clone repositories on a Windows host that has the .NET toolchain, since the sandbox itself doesn't. Covers authentication, starting/streaming/cancelling a run, downloading files, and listing/cloning repositories. Does NOT cover the dashboard, resource monitoring, install/upgrade, or repository deletion - those have no API surface for this caller at all.
---

# Out of the Box command service - client guide

Out of the Box is a Windows-hosted service that lets you (running in a sandboxed environment with no
local .NET toolchain) run `dotnet`/`git` commands, pull files, and list/clone repositories on a real
Windows host, over HTTPS. This skill documents exactly how to call it. It restates behavior from
`specs/dotnet-command-execution`, `specs/git-command-execution`, `specs/file-transfer`,
`specs/repository-management`, and `specs/service-authentication` - if the service's actual behavior
ever seems to contradict this doc, those spec files (not this one) are authoritative, and this doc
is stale.

**Scope**: this skill covers the six endpoints below and nothing else. It does not cover the
operator-facing dashboard, host/process resource monitoring, or install/upgrade - none of that has
any bearer-token-authenticated API surface for you to reach. **Repository deletion is explicitly out
of scope too and cannot be done from here at all** - it has no REST endpoint; it's a dashboard-only
action the human operator performs, by design (repository *listing* and *cloning* are in scope, see
below). If you need a repository deleted, ask the operator - don't look for an endpoint, there isn't
one.

## Authentication

Every endpoint below requires an `Authorization: Bearer <token>` header. The token value comes
from your own environment/configuration - the operator who set up your access to this service told
you what it is (e.g. as an environment variable in your sandbox); it is never hardcoded anywhere,
and this doc has no default to fall back on. A missing or invalid credential gets a plain `401
Unauthorized` with an empty body, before anything else about the request is even looked at (no
`X-Run-Id`, no validation of the body - nothing).

The examples below assume the token is in `$TOKEN` and the service's base URL (including port) is
in `$BASE_URL`, e.g.:

```bash
export TOKEN="<your bearer token>"
export BASE_URL="https://oob-host.example:5443"
```

The certificate is typically self-signed for this kind of deployment (see the operator for how to
trust/pin it - e.g. `curl --cacert <path-to-cert>`); the examples below use `-k`/`--insecure` only
as a placeholder for "however your operator told you to trust this connection."

## Starting a `dotnet` command: `POST /run`

```bash
curl -sk -N -D /tmp/headers.txt -X POST "$BASE_URL/run" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"arguments": ["test"], "workingDirectory": "my-repo", "timeoutSeconds": 300}'
```

Request body (JSON):

| Field             | Required | Meaning                                                                                       |
|--------------------|----------|-----------------------------------------------------------------------------------------------|
| `arguments`        | yes, non-empty | The `dotnet` argument list, e.g. `["test", "--filter", "Foo"]` - passed through as-is, unrestricted. |
| `workingDirectory`  | yes, non-empty | A path relative to the service's configured root (e.g. just the repository name, `"my-repo"`, or a subdirectory within it, `"my-repo/src/Project"`). Must resolve *inside* that root - an escaping or absolute path is rejected. |
| `timeoutSeconds`    | no       | Overrides the server's default execution timeout for this run only. Always clamped to a server-configured maximum - you can shorten the effective timeout freely, but can never request an effectively unbounded one. |

The response is `200 OK` with `Content-Type: text/event-stream`, and an `X-Run-Id` header set
**before the body starts streaming** - read it off the response headers immediately, don't wait for
the stream to finish. This is the run's id, needed later to cancel it (or to look it up in the
dashboard/history, which you as the sbx caller can't reach, but the operator can).

## Starting a `git` command: `POST /run/git`

Identical request/response shape to `POST /run` above - same JSON body fields, same `X-Run-Id`
header behavior, same SSE framing (see below). The only difference is which executable runs:

```bash
curl -sk -N -D /tmp/headers.txt -X POST "$BASE_URL/run/git" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"arguments": ["pull"], "workingDirectory": "my-repo"}'
```

**No git subcommand or flag is restricted** - `reset --hard`, `clean -fdx`, `push --force`, etc. all
run exactly as requested, including destructive ones. Nothing here second-guesses the command you
send; be as careful with `git` calls here as you would running them locally.

A `dotnet` run and a `git` run against the *same* repository share one lock, bidirectionally:
starting a `git pull` while a `dotnet test` is still running against that repository (or vice versa)
gets rejected as busy (see [Errors](#errors-and-rejections) below) - regardless of which kind was
already there. Two runs against *different* repositories never contend with each other.

## Consuming the stream (`/run`, `/run/git`)

The response body is Server-Sent Events - a chunked, line-based text stream, not a WebSocket. Each
event is:

```
event: <type>
data: <payload>

```

Event types:

| Event    | Payload                                              | Meaning                                                                 |
|----------|-------------------------------------------------------|--------------------------------------------------------------------------|
| `stdout` | one raw output line                                   | One line of the process's standard output, as it's produced.             |
| `stderr` | one raw output line                                   | One line of standard error, as it's produced.                            |
| `done`   | `{"exitCode": <int>, "truncated": <bool>}`             | Terminal - the process exited normally. `truncated` is true if output hit the server's size cap before the process finished (it kept running to completion regardless). |
| `error`  | `{"reason": "<reason>"}` or `{"reason": "validation", "runId": "<guid>"}` | Terminal - no exit code was ever produced. See [Errors](#errors-and-rejections). |

**You must read the response incrementally, not buffer it.** Buffering defeats the entire point of
streaming a long-running command's output as it happens, and for a genuinely long build/test run
you'd otherwise see nothing until it's already over:

- `curl`: pass `-N`/`--no-buffer` (as in the examples above).
- If you're driving this from a background process instead of a foreground `curl -N` (e.g. to poll
  intermittently rather than block your whole turn on one command), redirect the stream to a file
  and `tail -f`/re-read it, rather than buffering the whole response before acting on any of it.
- Any HTTP client works as long as it exposes the response body incrementally - the general
  principle is "don't wait for the connection to close before consuming bytes," not anything
  `curl`-specific.

Every response - success, timeout, cancellation, or rejection - ends the stream (no explicit
reconnect/retry framing is provided beyond that). Exactly one of `done` or `error` is the last
event you'll ever see for a given run.

## Requesting a file: `POST /files`

For pulling an actual output file a prior `dotnet`/`git` run produced (a built DLL, a test-results
file, etc.) - not for streamed console output, which you already got via SSE above. Despite the
name, this isn't limited to build artifacts - any file inside the repository is fair game, as long
as it resolves inside that repository's own directory.

```bash
curl -sk -D /tmp/headers.txt -X POST "$BASE_URL/files" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"repository": "my-repo", "path": "test-results/results.trx"}' \
  -o results.trx
```

Request body: `{"repository": "<same relative-path shape as workingDirectory above>", "path":
"<file path relative to that repository's own root>"}`. `X-Run-Id` is set on the response before the
body starts streaming, same as `/run`/`/run/git`.

**This is not SSE.** The response body is the raw file bytes (`Content-Type:
application/octet-stream`, `Content-Length` set), like any ordinary file download - stream it
straight to disk (`curl -o <file>`, or your HTTP client's equivalent of copying the response stream
directly to a file) rather than buffering a potentially large file fully in memory first.

`path` is confined to *that specific repository's own directory* - a path that escapes it (`../`,
an absolute path elsewhere on the host, a symlink that resolves outside it) is rejected the same
way an escaping `workingDirectory` is on `/run`. **Only request paths you already have good reason
to believe exist inside the repository you just ran a command against** (e.g. because you told
`dotnet test` to write its results there) - this endpoint does not offer directory listing, so
there's no way to discover what's available if you're guessing.

> **A subtlety worth knowing**: this service's own build output convention centralizes compiled
> output *outside* each project's directory (see its `CLAUDE.md`/`design.md` if you're ever
> inspecting this repo itself) - that pattern generalizes to any repository built here. If you need
> a file back via this endpoint, make sure whatever command produced it was told to write to an
> explicit path *inside* the repository (e.g. `dotnet test --results-directory <path-under-the-repository>`),
> not left to a default build-output location that might not be reachable from inside that
> repository's own directory tree at all.

## Listing repositories: `GET /repositories`

Returns every repository directly under the service's configured root, with the same metadata the
human operator's dashboard shows:

```bash
curl -sk -X GET "$BASE_URL/repositories" -H "Authorization: Bearer $TOKEN"
```

Response is `200 OK` with a JSON array, one object per repository:

| Field | Meaning |
|---|---|
| `name` | The repository's directory name - what you'd pass as (or as the prefix of) `workingDirectory`/`repository` on the other endpoints. |
| `isActive` | Whether a `dotnet`/`git` run or a clone currently holds this repository's lock. |
| `totalSizeBytes` / `statsComputed` | On-disk size in bytes; `statsComputed` is `false` briefly after a repository first appears, before the background sampler has measured it yet. |
| `isGitRepository`, `branch`, `isDirty`, `aheadCount`, `behindCount` | Git status, if this is a git checkout at all (`isGitRepository: false` and the rest `null` otherwise). |

This is a cache read (recomputed on a slow background cadence plus immediately after any run against
that repository finishes), not a live filesystem/git walk each call - cheap to call, but size/git
status can lag a just-finished run by a moment.

## Cloning a repository: `POST /repositories/clone`

```bash
curl -sk -D /tmp/headers.txt -X POST "$BASE_URL/repositories/clone" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com/some-repo.git", "name": "some-repo"}' \
  -o /dev/null
```

Request body: `{"url": "<any git-reachable URL>", "name": "<the directory name to clone into>"}`.
`name` is resolved and confined under the configured root the same way `workingDirectory` is
everywhere else - it must not already exist. Response is `202 Accepted` with `{"runId": "<guid>"}`
(also set as `X-Run-Id`) once the clone is accepted; the clone itself proceeds in the background the
same way a `git pull` does, and `GET /repositories` reflects the new repository once it's done (poll
it, or watch for the repository's name to appear).

**Note**: unlike a `dotnet`/`git`/file-transfer run id, a clone's run id is *not* accepted by
`POST /run/{runId}/cancel` - that endpoint always returns `404` for a repository-clone or
-delete run id, even one you started via this endpoint. There is no way to cancel an in-flight
clone from here; only the operator can, from the dashboard.

## Cancelling a run: `POST /run/{runId}/cancel`

One endpoint for every cancellable kind - a `dotnet` run, a `git` run, or an in-flight file
transfer, using the same `runId` you got from the `X-Run-Id` header when you started it:

```bash
curl -sk -X POST "$BASE_URL/run/$RUN_ID/cancel" -H "Authorization: Bearer $TOKEN"
```

- `202 Accepted` - cancellation was requested. The run's own stream (if you're still attached to
  it) will shortly emit a terminal `error` event with `"reason": "cancelled"` (for `/run`,
  `/run/git`) or simply end (for a file transfer, which has no SSE framing to write a
  terminal event into).
- `404 Not Found` - the run id is unknown to the server *right now*, or it's not a cancellable kind
  at all. This covers several distinct situations you can't tell apart from the response alone: the
  id never existed (e.g. a typo), the run already finished on its own before your cancel request
  arrived, the service process itself restarted since the run started (its in-memory tracking is
  not persisted across a restart), or the id belongs to a repository clone or delete - neither is
  ever cancellable through this endpoint, even a clone you started yourself via
  `POST /repositories/clone`.

## Errors and rejections

| Situation | What you see |
|---|---|
| Missing/invalid bearer token | `401 Unauthorized`, empty body, on any of the six endpoints - happens before anything else is checked. |
| `/run`, `/run/git`: empty `arguments` or blank `workingDirectory` | SSE `error` event, `{"reason": "validation"}`. Note this arrives *inside* a `200 OK` SSE stream, not as an HTTP-level 4xx - the HTTP response is already committed to streaming by the time this is checked, so watch the event, not the status code. |
| `/run`, `/run/git`: `workingDirectory` resolves outside the configured root | Same as above - SSE `error`, `{"reason": "validation"}`. |
| `/run`, `/run/git`: the target repository already has a run in flight | SSE `error`, `{"reason": "validation", "runId": "<the blocking run's id>"}` - the run already holding that repository's lock. Wait for it (or cancel it, if it's yours to cancel) before retrying. |
| `/run`, `/run/git`: hit the timeout | SSE `error`, `{"reason": "timeout"}`. |
| `/run`, `/run/git`: cancelled (by you, or the connection dropped) | SSE `error`, `{"reason": "cancelled"}`. |
| `/files`: missing `repository`/`path`, or either resolves outside its allowed root | `400 Bad Request`, empty body - checked, and responded to with a real HTTP status, *before* any streaming starts (unlike `/run`'s validation, this endpoint hasn't committed to a response yet at that point). |
| `/files`: the resolved file doesn't exist (or is a directory - this endpoint never lists directory contents) | `404 Not Found`, empty body. Distinct from the 400 case above: 404 means "the path itself is fine, nothing's there (yet, or ever)"; 400 means "the path itself isn't allowed." |
| `/repositories/clone`: missing `url`/`name`, or `name` resolves outside the configured root | `400 Bad Request`, `{"reason": "validation"}`. |
| `/repositories/clone`: `name` already exists | `409 Conflict`, `{"reason": "already-exists"}`. |
| `/repositories/clone`: target already has a run (or another clone) in flight | `409 Conflict`, `{"reason": "busy", "runId": "<the blocking run's id>"}`. |
| `/run/{runId}/cancel`: unknown/already-finished/pre-restart run id, or a repository clone/delete id | `404 Not Found` (see [Cancelling a run](#cancelling-a-run-post-runrunidcancel) above for the cases this covers). |

## Out of scope (do not look for these)

- **Repository deletion** - dashboard-only, no REST endpoint, by design (listing and cloning *are*
  in scope - see above). Ask the operator if a repository needs to go away.
- **The dashboard** (live status, run history, resource graphs) - a human-facing Blazor Server UI
  behind a separate cookie-based login, not reachable with your bearer token.
- **Host/process resource monitoring** (CPU/RAM, killing a stray process) - operator/dashboard-only.
- **Install/upgrade** - this is a deployment concern for whoever runs the service, not something
  this API exposes at all.
