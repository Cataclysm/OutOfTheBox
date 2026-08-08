# Installing / Running

## Today: development run

There is no packaged release yet (see [Planned](#planned-production-install) below). To run the service directly:

```
dotnet run --project src/OutOfTheBox.Host
```

Configuration is read from `src/OutOfTheBox.Host/appsettings.json` (and `appsettings.Development.json` in the `Development` environment), with environment-variable overrides available via the standard ASP.NET Core convention, e.g.:

```
OutOfTheBox__BearerToken=some-token dotnet run --project src/OutOfTheBox.Host
```

At minimum you'll need to set:

- `OutOfTheBox:RootDirectory` — the absolute path repos will be resolved under
- `OutOfTheBox:BearerToken` — the shared credential callers must present

See `ServiceOptions` (`src/OutOfTheBox.Application/Configuration/ServiceOptions.cs`) for the full configuration surface (timeouts, output cap, SQLite path — some of these aren't wired up to real behavior yet; check `openspec/changes/sbx-dotnet-command-service/tasks.md` for current status).

## Network & transport

The command API (`POST /run`, `POST /run/git`, `POST /artifacts`, `POST /run/{runId}/cancel`) and
the dashboard share the same Kestrel HTTPS endpoint and port (`5443` by default, per
`appsettings.json`'s `Kestrel:Endpoints:Https:Url`). The bearer token, command arguments/output,
and the dashboard's cookie session all cross this connection, so the service refuses to start if
any configured Kestrel endpoint isn't `https://` (see `Program.cs`) — there is no supported way to
run this service over plain HTTP.

### Certificate

A private/self-signed certificate is sufficient for v1: both the OutOfTheBox host and the sbx
sandbox caller are under the same operator's control, not exposed to the public internet. Generate
one and bind it to Kestrel via the standard ASP.NET Core configuration shape, e.g.:

```
dotnet dev-certs https -ep C:\ProgramData\OutOfTheBox\outofthebox.pfx -p <password>
```

(or any equivalent self-signed cert generated with `New-SelfSignedCertificate` / `openssl`), then
point Kestrel at it:

```json
"Kestrel": {
  "Endpoints": {
    "Https": {
      "Url": "https://0.0.0.0:5443",
      "Certificate": {
        "Path": "C:\\ProgramData\\OutOfTheBox\\outofthebox.pfx",
        "Password": "<password>"
      }
    }
  }
}
```

Because the certificate isn't from a publicly trusted CA, the sbx-side caller must pin/trust it
explicitly rather than relying on the OS/CA trust store — e.g. for `curl`, pass `--cacert
<path-to-the-cert-in-PEM-form>` (or `-k`/`--insecure` only if the operator has independently
verified the connection is otherwise safe, such as over a private network the operator controls
end-to-end); for a .NET `HttpClient`, configure an `HttpClientHandler.ServerCertificateCustomValidationCallback`
that compares the presented certificate's thumbprint against the known one, rather than disabling
validation outright.

### Firewall

Restrict inbound connections on the configured port to exactly the two clients that need it:

- The sbx sandbox's IP (or IP range), for the command API endpoints (`/run`, `/run/git`,
  `/artifacts`, `/run/{runId}/cancel`) — these all live on the same port as the dashboard, so a
  port-level firewall rule can't separate "command API access" from "dashboard access" by itself.
- The operator's own network/IP, for the dashboard.

On Windows, this is a single inbound rule scoped by remote address, e.g.:

```powershell
New-NetFirewallRule -DisplayName "OutOfTheBox" -Direction Inbound -Protocol TCP -LocalPort 5443 `
    -RemoteAddress <sbx-sandbox-ip>,<operator-ip> -Action Allow
```

`install.ps1` (see [Planned: production install](#planned-production-install)) will create this
rule automatically; the command above documents the equivalent manual step in the meantime.

### Consuming Server-Sent Events (`/run`, `/run/git`)

`POST /run` and `POST /run/git` respond with `Content-Type: text/event-stream` and flush each
`stdout`/`stderr` line as it's produced, ending with a `done` (or `error`) event carrying the exit
code — the connection is a normal chunked HTTP response, not a WebSocket, so any HTTP client works
as long as it doesn't buffer the full response before returning control to the caller:

- `curl`: pass `-N`/`--no-buffer` (otherwise curl waits for the connection to close before
  printing anything, defeating the point of streaming output from a long-running command).
- .NET `HttpClient`: pass `HttpCompletionOption.ResponseHeadersRead` to `SendAsync`, then read the
  response stream incrementally (e.g. via a `StreamReader` over `content.ReadAsStreamAsync()`)
  rather than awaiting `ReadAsStringAsync()`, which buffers the entire body first.
- Any other client: equivalent "don't wait for EOF before consuming bytes" behavior is required.

### Downloading artifacts (`/artifacts`)

`POST /artifacts` is not SSE — the response body is the raw file bytes (with the resolved file's
actual content type), the same as any ordinary file download. The same non-buffering principle
still applies for a large artifact: prefer streaming the response body to disk (e.g. `curl -o
<file>`, or `HttpClient` + `content.CopyToAsync(fileStream)`) over loading the whole response into
memory first. A request for a path outside the named repo's own directory (path traversal,
symlink escape, or an absolute path elsewhere on the host) is rejected before any file is opened —
per the same two-level path-confinement policy the working-directory resolution already applies to
`/run`/`/run/git` — with a distinct "confinement violation" outcome, separate from a plain
"file not found" for a path that's legitimately inside the repo but doesn't exist yet.

## Production install

### 1. Publish

```
dotnet publish src/OutOfTheBox.Host -p:PublishProfile=win-x64 -o <publish-output-dir>
```

Produces a self-contained, single-file `OutOfTheBox.Host.exe` (~110 MB — it bundles the full .NET
runtime, so the target machine needs nothing pre-installed beyond the OS itself and `git` on
`PATH`) alongside a `wwwroot/` folder of static web assets (including the vendored Chart.js). Both
the exe and `wwwroot/` must be copied together — `install.ps1`/`upgrade.ps1` below do this for you.

### 2. Install

```powershell
.\scripts\install.ps1 -SourcePath <publish-output-dir> -RepoRootDirectory C:\repos `
    -AllowedRemoteAddresses <sbx-sandbox-ip>,<operator-ip>
```

Must be run elevated (`#Requires -RunAsAdministrator`). This:

- Creates a dedicated local service account (`svc-outofthebox` by default) — **not** local admin,
  **not** a pre-existing/shared account — and grants it exactly: log-on-as-a-service (granted
  automatically by `sc.exe create ... obj=` — no separate step needed), read/write on the data
  directory and the configured repo root (via `icacls`, not broader filesystem access), and
  "Performance Monitor Users" membership (required for `PerformanceCounter` access to the
  `Processor` category — without it, host/process resource monitoring silently fails).
- Creates the install directory (`C:\Program Files\OutOfTheBox` by default — disposable, replaced
  wholesale by every `upgrade.ps1` run) and the data directory (`%ProgramData%\OutOfTheBox` by
  default — config + the SQLite file, **never** touched by upgrade or reinstall, so history and
  configuration survive both).
- Writes the real production `appsettings.json` into the data directory (root directory, bearer
  token — auto-generated and printed once if not supplied — port, timeouts, output cap, SQLite
  path, and the Kestrel HTTPS certificate binding); the exe reads it via the
  `OUTOFTHEBOX_DATA_DIR` environment variable, set on the service's own registry entry so it
  doesn't depend on being present in a global environment.
- Generates a self-signed certificate via `dotnet dev-certs` if `-CertificatePath` isn't supplied
  (see [Network & transport](#network--transport) above for how the sbx-side client must then pin
  it).
- Registers the Windows Service and explicitly configures SCM crash-recovery (`sc.exe failure ...
  actions= restart/60000/restart/60000/restart/60000`) — **SCM does not restart a crashed service
  by default**, this has to be set explicitly.
- Opens the firewall rule scoped to `-AllowedRemoteAddresses`, starts the service, and verifies
  `git` is resolvable on the service account's own `PATH` (not just the installer's interactive
  session) — a service process inherits the account's environment, not necessarily yours.

### 3. Upgrade

```powershell
.\scripts\upgrade.ps1 -SourcePath <new-publish-output-dir>
```

Stops the service (aborting with a clear error if it doesn't reach `Stopped` within the timeout,
rather than touching the install directory while the old process might still hold file handles),
replaces the install directory's contents with the new build, starts the service, and polls
`/version` to confirm the new build actually came up. The data directory is never referenced by
this script at all — that's what makes "upgrade = replace the exe" safe to re-run and guarantees
configuration and history (including resource samples, across every run kind) survive every
upgrade.

**Downgrade is unsupported** — EF Core migrations are forward-only, so rolling the exe back to an
older build against a SQLite file a newer migration has already touched is not a supported path.
If you want a manual rollback point before upgrading, copy the data directory's `outofthebox.db`
(and its `-wal`/`-shm` sidecars, if present) somewhere safe first.

### After a restart

The run registry (which run ids are cancellable, which repos are locked) is **in-memory only** —
this applies uniformly to every run kind (`dotnet`/`git` commands, clones, deletes, transfers).
After any restart (crash-recovery, `upgrade.ps1`, or a manual restart):

- A `POST /run/{runId}/cancel` for a run id from before the restart returns 404 — the registry
  entry that tracked it is gone.
- Repos that were locked (a run in flight against them) before the restart become immediately
  available again — there is no persisted lock state to reconcile.
- Any run still recorded as `Running` in SQLite from before the restart is reconciled to
  `Interrupted` at startup, since the in-memory state that would confirm it's still actually
  running was lost with the old process.
