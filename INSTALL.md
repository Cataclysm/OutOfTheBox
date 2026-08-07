# Installing / Running

## Today: development run

There is no packaged release yet (see [Planned](#planned-production-install) below). To run the service directly:

```
dotnet run --project src/Host/BuildAndTestService.Host
```

Configuration is read from `src/Host/BuildAndTestService.Host/appsettings.json` (and `appsettings.Development.json` in the `Development` environment), with environment-variable overrides available via the standard ASP.NET Core convention, e.g.:

```
BuildAndTestService__BearerToken=some-token dotnet run --project src/Host/BuildAndTestService.Host
```

At minimum you'll need to set:

- `BuildAndTestService:RootDirectory` — the absolute path repos will be resolved under
- `BuildAndTestService:BearerToken` — the shared credential callers must present

See `ServiceOptions` (`src/Application/BuildAndTestService.Application/Configuration/ServiceOptions.cs`) for the full configuration surface (timeouts, output cap, SQLite path — some of these aren't wired up to real behavior yet; check `openspec/changes/sbx-dotnet-command-service/tasks.md` for current status).

## Planned: production install

Once `tasks.md` Section 14 (Packaging & Install/Upgrade) is implemented, this section will describe:

- Publishing `Host` as a self-contained single-file win-x64 executable (no separate .NET runtime install needed on the target machine)
- Running `install.ps1`, which will:
  - Create a dedicated, least-privileged local Windows service account (not local admin, not a pre-existing account)
  - Install the service under a per-machine data directory separate from the binary's install directory, so upgrades never touch configuration or history
  - Register it as a Windows Service with SCM crash-recovery configured (restart on crash — not automatic by default)
  - Open the necessary firewall rule(s)
- Running `upgrade.ps1` for subsequent releases: stop, swap the binary, start, verify via a `/version` endpoint — configuration and SQLite history are untouched

This section will be rewritten with concrete instructions once that work lands — see `design.md`'s Packaging decisions for the full rationale in the meantime.
