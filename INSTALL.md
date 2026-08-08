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

Packaged as a WiX Toolset installer: a Burn bootstrapper (`OutOfTheBoxSetup.exe`) that ensures the
.NET 10 SDK and Git for Windows are present (installing either one silently if missing, skipping
the check entirely if already satisfied), then runs the actual MSI.

### 1. Publish

```
dotnet publish src/OutOfTheBox.Host -p:PublishProfile=win-x64 -o artifacts/publish/OutOfTheBox.Host
```

Produces a self-contained, single-file `OutOfTheBox.Host.exe` (~110 MB — it bundles the full .NET
runtime; the target machine needs nothing pre-installed beyond the OS itself, since the bootstrapper
below handles the SDK/`git` prerequisites) alongside a `wwwroot/` folder of static web assets
(including the vendored Chart.js) — the MSI's file harvesting picks up everything under this output
directory automatically, so there's no separate copy step to remember.

### 2. Package

```
dotnet build installer/OutOfTheBox.Msi -c Release
dotnet build installer/OutOfTheBox.Bootstrapper -c Release
```

Produces `installer/OutOfTheBox.Msi/bin/x64/Release/OutOfTheBox.Msi.msi` (~40 MB, cabinet embedded)
and `installer/OutOfTheBox.Bootstrapper/bin/x64/Release/OutOfTheBox.Bootstrapper.exe` (the thing an
operator actually runs). Both projects are deliberately outside `OutOfTheBox.slnx` and outside the
repo's Central Package Management (`installer/Directory.Packages.props` opts out) — WiX's own
project/package conventions don't fit either cleanly, and this keeps `dotnet build`/`dotnet test` on
the main solution unaffected by installer changes. `WixToolset.Sdk` requires accepting the WiX v7
"Open Source Maintenance Fee" EULA once per machine (`wix eula accept wix7`, or pass
`-p:AcceptEula=wix7`) — see [wixtoolset's OSMF terms](https://docs.firegiant.com/wix/osmf/) before
building on a machine that hasn't accepted it; the terms are free unless the building organization's
annual revenue exceeds $10,000.

### 3. Install

Run `OutOfTheBoxSetup.exe` elevated. The MSI has its own interactive config page (repo root
directory, bearer token, port) verified working when the MSI is run directly
(`msiexec /i OutOfTheBox.Msi.msi`, or double-clicking it) — inspected via the Windows Installer COM
API directly (Dialog/ControlEvent tables), not just "the build succeeded". **Whether that same page
is shown when installing through the bootstrapper is not yet verified on a real machine**: Burn's
standard bootstrapper application runs chained MSI packages at a reduced UI level by default, so it
may run this MSI silently rather than showing its dialog. Until that's confirmed (or a bootstrapper-
level UI is built instead), pass the same properties on the bootstrapper's own command line, which
Burn forwards through to the chained MSI:

```
OutOfTheBoxSetup.exe REPOROOTDIR="C:\repos" PORTNUMBER=5443
```

**The bearer token doesn't need to be supplied** — a cryptographically random one is generated
automatically on first install (shown pre-filled in the config page, or silently resolved on a
fully unattended install) and preserved automatically on every subsequent upgrade, so operators
don't need to remember or re-enter it. Pass `BEARERTOKEN="<a value you choose>"` explicitly (either
on the command line, or by editing the field in the dialog) only if you want a specific token
instead of the generated one — an explicit value always takes precedence, on a fresh install or an
upgrade alike. The service account's own Windows logon password is generated and preserved the same
way, entirely internally — there's nothing to supply for it.

This:

- Detects and, if missing, silently installs the .NET 10 SDK and Git for Windows (each is a
  separate, `Permanent="yes"` chained package — uninstalling OutOfTheBox later does **not** remove
  either, since other things on the host may depend on them too).
- Creates a dedicated local service account (`svc-outofthebox`, via the WiX Util extension's
  `util:User`) — **not** local admin, **not** a pre-existing/shared account — with log-on-as-a-service
  (granted automatically by service creation), and "Performance Monitor Users" membership (required
  for `PerformanceCounter` access to the `Processor` category — without it, host/process resource
  monitoring silently fails), plus read/write on the data directory and the configured repo root.
- Installs to `C:\Program Files\OutOfTheBox` (disposable, replaced wholesale by every upgrade) and
  creates `%ProgramData%\OutOfTheBox` (the data directory — config + the SQLite file, **never**
  touched by upgrade, uninstall, or reinstall, so history and configuration always survive all
  three) — the data directory has no file `Component` referencing it at all in the MSI, so this
  isn't just policy, it's structural.
- Writes `REPOROOTDIR`/`BEARERTOKEN`/`PORTNUMBER` into the Windows Service's own `Environment`
  registry value alongside `OUTOFTHEBOX_DATA_DIR`, reusing the same environment-variable
  configuration override `Program.cs` already supports — no separate config file for the installer
  to write.
- Registers the Windows Service and explicitly configures SCM crash-recovery (`util:ServiceConfig`,
  restart × 3 with a 60s delay) — **SCM does not restart a crashed service by default**, this has to
  be set explicitly regardless of which tool sets it.
- Opens a firewall rule for the configured port (narrow it to specific remote addresses afterward —
  see [Network & transport](#network--transport) above — the installer doesn't know the sbx
  sandbox's or operator's IPs at install time).
- Re-checks `git` is resolvable via the registry as a `Launch` condition, failing loudly if
  somehow still missing even after the bootstrapper's own check (e.g. the MSI was run directly,
  bypassing the bootstrapper).

Certificate binding for Kestrel HTTPS is not yet wired into the installer — see
[Network & transport](#network--transport) above for generating one manually in the meantime.

### 4. Upgrade / uninstall

Run a newer build's `OutOfTheBoxSetup.exe` — `MajorUpgrade` handles it natively (WiX sequences the
old version's removal and the new version's install around the service's own stop/start), so this
is the same command as a fresh install, not a separate script. Configuration and history (including
resource samples, across every run kind) survive, per the data-directory guarantee above.

Uninstall via Programs and Features (or `OutOfTheBoxSetup.exe /uninstall`) removes the installed
files, the Windows Service, and the dedicated service account — but, same as upgrade, never touches
the data directory. Reinstalling recovers prior history automatically.

**Downgrade is unsupported** — EF Core migrations are forward-only, so rolling back to an older
build against a SQLite file a newer migration has already touched is not a supported path. If you
want a manual rollback point before upgrading, copy the data directory's `outofthebox.db` (and its
`-wal`/`-shm` sidecars, if present) somewhere safe first.

### After a restart

The run registry (which run ids are cancellable, which repos are locked) is **in-memory only** —
this applies uniformly to every run kind (`dotnet`/`git` commands, clones, deletes, transfers).
After any restart (crash-recovery, an upgrade, or a manual restart):

- A `POST /run/{runId}/cancel` for a run id from before the restart returns 404 — the registry
  entry that tracked it is gone.
- Repos that were locked (a run in flight against them) before the restart become immediately
  available again — there is no persisted lock state to reconcile.
- Any run still recorded as `Running` in SQLite from before the restart is reconciled to
  `Interrupted` at startup, since the in-memory state that would confirm it's still actually
  running was lost with the old process.
