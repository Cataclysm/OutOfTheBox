## Why

The dashboard's resource graphs (Status page, and a run's detail page in History) only ever showed CPU and RAM - an operator diagnosing a run that's slow because of network I/O (a slow package restore, a large `git fetch`) or disk I/O (a large build's intermediate output, an antivirus scan colliding with a build) had no way to see that from this dashboard at all. Per direct instruction: add network and disk I/O as first-class graphs alongside CPU/RAM, clean up the existing graphs' legends (redundant on a single-line chart), and rework both pages' graph layout while at it.

## What Changes

- **Disk I/O sampling**: a new host-wide disk read/write bytes-per-second figure, sourced from the `PhysicalDisk`/`_Total` performance counter (Windows' own all-drives-combined instance - no per-drive enumeration/summing needed, unlike network's per-NIC-interface counters), sampled on the same cadence as everything else in `host-resource-monitoring`.
- **Network and disk figures are now tagged onto every run's own resource-sample point/row**, not just the host series - neither can be isolated to one process tree on Windows (there is no per-process network-only counter, and disk I/O is a whole-machine category), so every run gets that same tick's host-level readings, the same "no isolated figure is possible, use the host's" precedent already established for a file transfer's CPU/RAM (a transfer has no process tree of its own either).
- **Persistence**: `RunResourceSample` gains four new nullable columns (network sent/received, disk read/write bytes-per-second) - nullable because a sample recorded before this change genuinely has no value for them, not a real zero reading.
- **Presentation - legends**: the CPU and RAM graphs (wherever they appear) no longer show a legend (redundant with their own heading, since each is a single line); the new Network and Disk I/O graphs do show one (each has two lines - Sent/Received, Read/Write - that can't be told apart without it).
- **Presentation - layout**:
  - The Status page's host graphs are now three rows instead of two: (1) CPU alone, scaled to the full row width, showing 20 minutes of live history instead of the usual 10; (2) CPU per core and RAM, 10 minutes each; (3) Network and Disk I/O, 10 minutes each.
  - A run's detail page (in History) now shows two rows instead of one: CPU and RAM, then Network and Disk I/O - covering that run's entire recorded duration, the same as CPU/RAM already did.
- The live rolling buffer backing the Status page's graphs is extended from 10 to 20 minutes of retention (so the host CPU graph's own longer window has real data to show); every other live graph continues to only display the most recent 10 minutes of it.
- **A run's detail page graphs now update live while the run is still in flight**, using the same `ResourceHistoryBuffer`/`IResourceEventBus` mechanism the Status page's live graphs already use - previously this page only ever showed a static snapshot of whatever was persisted at page load. Per direct instruction, this graph's dataset keeps growing for as long as the run stays open rather than sliding a fixed window like every Status-page live graph does. A run viewed before it has any recorded samples transitions from an empty-state message straight to the (now-live) graphs the moment its first sample arrives, rather than needing a manual reload.

## Capabilities

### Modified Capabilities
- `host-resource-monitoring`: adds disk I/O as a sampled host figure, and extends network/disk tagging to every tracked run, not just the host series.
- `run-history`: `RunResourceSample`'s persisted series gains network/disk alongside CPU/RAM.
- `service-dashboard`: reworks the resource-graph presentation - legend visibility, the Status page's three-row layout with the host CPU graph's own longer window, and a run's detail page's two-row layout.

## Impact

- **Affected code**: `HostResourceSampler` (Infrastructure, new `PhysicalDisk` counters), `HostResourceSamplerService` (network/disk tagging onto every tracked run and transfer), `ResourceSnapshot`/`ResourceHistoryBuffer`/`ResourceHistoryPoint` (Application, new fields, buffer window bump, new windowed `Get` overload), `RunResourceSample` (Domain, new nullable columns) plus a new EF Core migration, `IChartInterop`/`ChartInterop`/`chart-interop.js` (a configurable per-chart live-data window), `LiveResourceGraph.razor` (Status page layout) and `RunHistoryGraph.razor` (run-detail layout).
- **No MCP-surface change**: `get_run_resources` stays CPU/RAM-only, per explicit scope - this change is dashboard-only.
- **No schema-breaking change**: the new `RunResourceSamples` columns are nullable additions; existing rows are unaffected and simply have no recorded network/disk figures.
