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

## Planned: production install

Once `tasks.md` Section 17 (Packaging & Install/Upgrade) is implemented, this section will describe:

- Publishing `Host` as a self-contained single-file win-x64 executable (no separate .NET runtime install needed on the target machine)
- Running `install.ps1`, which will:
  - Create a dedicated, least-privileged local Windows service account (not local admin, not a pre-existing account)
  - Install the service under a per-machine data directory separate from the binary's install directory, so upgrades never touch configuration or history
  - Register it as a Windows Service with SCM crash-recovery configured (restart on crash — not automatic by default)
  - Open the necessary firewall rule(s)
- Running `upgrade.ps1` for subsequent releases: stop, swap the binary, start, verify via a `/version` endpoint — configuration and SQLite history are untouched

This section will be rewritten with concrete instructions once that work lands — see `design.md`'s Packaging decisions for the full rationale in the meantime.
