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

- `OutOfTheBox:RootDirectory` — the absolute path repositories will be resolved under
- `OutOfTheBox:BearerToken` — the shared credential callers must present

See `ServiceOptions` (`src/OutOfTheBox.Application/Configuration/ServiceOptions.cs`) for the full configuration surface (timeouts, output cap, SQLite path — some of these aren't wired up to real behavior yet; check `openspec/changes/sbx-dotnet-command-service/tasks.md` for current status).

## Network & transport

The MCP server (`/mcp`, MCP Streamable HTTP transport) and the dashboard share the same Kestrel
HTTPS endpoint and port (`5443` by default, per `appsettings.json`'s `Kestrel:Endpoints:Https:Url`).
The bearer token, MCP tool call arguments/output, and the dashboard's cookie session all cross this
connection, so the service refuses to start if any configured Kestrel endpoint isn't `https://` (see
`Program.cs`) — there is no supported way to run this service over plain HTTP.

### Certificate

A private/self-signed certificate is sufficient for v1: both the Out of the Box host and the sbx
sandbox caller are under the same operator's control, not exposed to the public internet.

**The [production install](#production-install) below generates and configures this
automatically** — the installer's own `ResolveSecrets` custom action creates a self-signed
certificate (covering the machine's hostname, `localhost`, and its local IPv4 addresses) the first
time it installs, writes it to `%ProgramData%\OutOfTheBox\outofthebox.pfx`, and points Kestrel at
it via the same environment-variable mechanism used for every other setting — no manual step
required, and an upgrade never regenerates or invalidates an already-issued certificate (the same
generate-once-then-preserve reasoning already applied to the bearer token and service account
password). This was not always true: an earlier real install crashed outright on startup ("No
server certificate was specified") because nothing configured a certificate at all — that gap is
what this automation closes.

For the [development run](#today-development-run) above (`dotnet run`, no installer involved),
generate one yourself and bind it via the standard ASP.NET Core configuration shape:

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

This is also how to supply your own certificate instead of the installer-generated one for a
production install: replace `%ProgramData%\OutOfTheBox\outofthebox.pfx` with your own file before
first install (the installer never overwrites an existing one), or update the service's own
`Kestrel__Endpoints__Https__Certificate__*` environment variables afterward and restart it.

Because the certificate isn't from a publicly trusted CA, the sbx-side caller must pin/trust it
explicitly rather than relying on the OS/CA trust store — e.g. for `curl`, pass `--cacert
<path-to-the-cert-in-PEM-form>` (or `-k`/`--insecure` only if the operator has independently
verified the connection is otherwise safe, such as over a private network the operator controls
end-to-end); for a .NET `HttpClient`, configure an `HttpClientHandler.ServerCertificateCustomValidationCallback`
that compares the presented certificate's thumbprint against the known one, rather than disabling
validation outright.

### Firewall

Restrict inbound connections on the configured port to exactly the two clients that need it:

- The sbx sandbox's IP (or IP range), for the MCP server (`/mcp`) — it lives on the same port as the
  dashboard, so a port-level firewall rule can't separate "MCP access" from "dashboard access" by
  itself.
- The operator's own network/IP, for the dashboard.

On Windows, this is a single inbound rule scoped by remote address, e.g.:

```powershell
New-NetFirewallRule -DisplayName "Out of the Box" -Direction Inbound -Protocol TCP -LocalPort 5443 `
    -RemoteAddress <sbx-sandbox-ip>,<operator-ip> -Action Allow
```

The [production install](#production-install) below already opens this port for you (scoped to any
address, since the installer doesn't know the sbx sandbox's or operator's IPs at install time) - the
command above documents narrowing that to specific remote addresses afterward.

### Connecting an MCP client

The MCP server needs no special transport handling the way SSE did - an MCP-aware client (Claude
Code configured with a remote MCP server) handles the Streamable HTTP transport itself. Point it at
`https://<host>:<port>/mcp` with an `Authorization: Bearer <token>` header; tool discovery, calling,
and result parsing are all handled by the client library, not something this deployment doc needs to
walk through. `dotnet_run`/`git_run`/`clone_repository` return a run id immediately - poll
`read_run_output` for incremental output and the eventual exit code, rather than expecting a single
call to block until the command finishes. `transfer_file` is the one synchronous tool, returning a
file's contents (base64-encoded) directly, confined to the named repository's own directory (path
traversal, symlink escape, or an absolute path elsewhere on the host is rejected before any file is
opened) and rejected outright if it exceeds the configured size limit, rather than truncated.

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
and `installer/OutOfTheBox.Bootstrapper/bin/x64/Release/OutOfTheBoxSetup.exe` (the thing an
operator actually runs — the bootstrapper project's own `OutputName` renames it from the project's
`OutOfTheBox.Bootstrapper` assembly name to this operator-facing one). Both projects are
deliberately outside `OutOfTheBox.slnx` and outside the repository's Central Package Management
(`installer/Directory.Packages.props` opts out) — WiX's own project/package conventions don't fit
either cleanly, and this keeps `dotnet build`/`dotnet test` on
the main solution unaffected by installer changes. `WixToolset.Sdk` requires accepting the WiX v7
"Open Source Maintenance Fee" EULA once per machine (`wix eula accept wix7`, or pass
`-p:AcceptEula=wix7`) — see [wixtoolset's OSMF terms](https://docs.firegiant.com/wix/osmf/) before
building on a machine that hasn't accepted it; the terms are free unless the building organization's
annual revenue exceeds $10,000.

### 3. Install

Run `OutOfTheBoxSetup.exe` elevated. It shows its own welcome screen first (installing the .NET
SDK/Git for Windows prerequisites if either is missing), then hands off to the MSI's own interactive
config page (repository root directory, bearer token, port) - `bal:DisplayInternalUICondition` on the
chained `MsiPackage` (scoped to `WixBundleAction = 6`, i.e. only during an actual install/upgrade,
never uninstall/modify/repair) is what makes that page reachable through the bootstrapper at all;
without it, Burn's standard bootstrapper application runs a chained MSI silently by default. Verified
both ways - running the MSI directly (`msiexec /i OutOfTheBox.Msi.msi`, or double-clicking it) and
through the bootstrapper - via the Windows Installer COM API directly (Dialog/ControlEvent tables),
not just "the build succeeded". The config page's title shows the version being installed
(`Configure Out of the Box vX.Y.Z`), and on an upgrade an additional "Upgrading from version X.Y.Z"
line appears above it, read back from the prior install's own registry-persisted version. The
bootstrapper, MSI Add/Remove Programs entry, and dashboard (favicon, header, login page) all share
the same brand mark, so the product looks consistent end to end.

If you ever need to skip the interactive page (e.g. a fully unattended install), pass the same
properties on the bootstrapper's own command line, which Burn forwards through to the chained MSI:

```
OutOfTheBoxSetup.exe REPOSITORYROOTDIR="C:\repositories" PORTNUMBER=5443
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
  separate, `Permanent="yes"` chained package — uninstalling Out of the Box later does **not** remove
  either, since other things on the host may depend on them too).
- Creates a dedicated local service account (`svc-outofthebox`, via the WiX Util extension's
  `util:User`) — **not** local admin, **not** a pre-existing/shared account — with log-on-as-a-service
  (granted automatically by service creation), and "Performance Monitor Users" membership (required
  for `PerformanceCounter` access to the `Processor` category — without it, host/process resource
  monitoring silently fails), plus read/write on the data directory and the configured repository root.
- Installs to `C:\Program Files\OutOfTheBox` (disposable, replaced wholesale by every upgrade) and
  creates `%ProgramData%\OutOfTheBox` (the data directory — config + the SQLite file, **never**
  touched by upgrade, uninstall, or reinstall, so history and configuration always survive all
  three) — the data directory has no file `Component` referencing it at all in the MSI, so this
  isn't just policy, it's structural.
- Writes `REPOSITORYROOTDIR`/`BEARERTOKEN`/`PORTNUMBER` into the Windows Service's own `Environment`
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

Certificate binding for Kestrel HTTPS is generated and wired up automatically — see
[Network & transport](#network--transport) above for how, and how to supply your own instead.

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

The run registry (which run ids are cancellable, which repositories are locked) is **in-memory only** —
this applies uniformly to every run kind (`dotnet`/`git` commands, clones, deletes, transfers).
After any restart (crash-recovery, an upgrade, or a manual restart):

- A `cancel_run` call for a run id from before the restart is rejected as unknown — the registry
  entry that tracked it is gone.
- Repositories that were locked (a run in flight against them) before the restart become immediately
  available again — there is no persisted lock state to reconcile.
- Any run still recorded as `Running` in SQLite from before the restart is reconciled to
  `Interrupted` at startup, since the in-memory state that would confirm it's still actually
  running was lost with the old process.
