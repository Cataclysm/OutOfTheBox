## 1. Domain/Application types

- [x] 1.1 `HostResourceSample` (Application): add `DiskReadBytesPerSecond`/`DiskWriteBytesPerSecond` (default `0`, appended after the existing network fields so every pre-existing positional call site keeps compiling).
- [x] 1.2 `RunResourceSample` (Domain): add four nullable properties - `NetworkBytesSentPerSecond`, `NetworkBytesReceivedPerSecond`, `DiskReadBytesPerSecond`, `DiskWriteBytesPerSecond`.
- [x] 1.3 `ResourceHistoryPoint` (Application): add nullable `DiskReadBytesPerSecond`/`DiskWriteBytesPerSecond`; doc comment updated to explain network/disk are host-measured but tagged onto every series now, not host-only like per-core.
- [x] 1.4 `ResourceHistoryBuffer`: `WindowDuration` 10 → 20 minutes; `Add` gains `diskReadBytesPerSecond`/`diskWriteBytesPerSecond` optional parameters; new `Get(string seriesKey, TimeSpan? window = null)` overload restricting the result to points no older than `window` before now, without evicting from the buffer itself.

## 2. Infrastructure: sampling and tagging

- [x] 2.1 `HostResourceSampler`: two new `PerformanceCounter`s (`PhysicalDisk`/`Disk Read Bytes/sec`+`Disk Write Bytes/sec`, instance `"_Total"` - Windows' own all-drives-combined instance, no per-drive enumeration needed unlike Network Interface), primed with a discarded first `NextValue()` call like every other counter here, disposed alongside the rest.
- [x] 2.2 `HostResourceSamplerService.TickCoreAsync`: host buffer `Add` now passes disk figures too; every tracked run's buffer `Add` and persisted `RunResourceSample` now also carry that tick's host-level network/disk figures (previously CPU/RAM only); the in-flight-transfer tagging path carries the same four figures alongside its existing host-tagged CPU/RAM.

## 3. Persistence

- [x] 3.1 New EF Core migration (`AddNetworkDiskToRunResourceSamples`) - four nullable `REAL` columns added to `RunResourceSamples`, no existing column touched. No `OutOfTheBoxDbContext.OnModelCreating` change needed (plain nullable scalar properties map by convention).

## 4. Presentation: chart interop

- [x] 4.1 `IChartInterop.CreateLineChartAsync`/`ChartInterop`: new optional `liveWindow` (`TimeSpan?`) parameter, forwarded to JS as milliseconds. Omitted/`null` means never trim (a run's own history graph, section 5 below) - a chart wanting a fixed rolling window (every Status page graph, section 6) passes one explicitly.
- [x] 4.2 `chart-interop.js`: per-canvas `windowMs` map (was a single hardcoded `WINDOW_MS` constant applied to every chart unconditionally) set at `createLineChart` time only when a window was actually passed, consulted by `pushPoint`'s trim logic (skipped entirely when no window is set for that canvas) instead of always falling back to a default; cleared on `destroyChart`.

## 5. Presentation: run-detail graphs (`RunHistoryGraph.razor`)

- [x] 5.1 CPU/RAM charts: `showLegend: false` (previously omitted, defaulting to `true`).
- [x] 5.2 New Network (Sent/Received) and Disk I/O (Read/Write) charts, `ChartValueFormat.BytesPerSecond`, legend on (two lines each).
- [x] 5.3 Layout: two explicit `.resource-graphs` rows (CPU+RAM, then Network+Disk I/O) instead of one, mirroring `LiveResourceGraph`'s own existing explicit-rows precedent.
- [x] 5.4 `DisposeAsync` destroys the two new canvases too.
- [x] 5.5 **Added per direct follow-up instruction**: live updates while the run is in flight. Injects `ResourceHistoryBuffer`/`IResourceEventBus` (same as `LiveResourceGraph`); subscribes unconditionally on first render; on each tick, reads `HistoryBuffer.Get(RunId.ToString())`'s latest point and `PushPointAsync`s it to all four charts if newer than the last one pushed. None of the four charts pass a `liveWindow` - the dataset keeps growing for the run's whole (still-open) duration, no fixed window, per direct instruction.
- [x] 5.6 A run with zero persisted samples at page-load time (viewed within its first tick or two) shows the empty-state message, not the charts, until its first sample arrives - `_hasData`/`_chartsCreated` flags (distinct from bUnit's `firstRender`) gate chart creation to whichever render pass first has data, re-querying the persisted series (not the live buffer) the first time a tick is observed with none yet, then letting the normal seed-then-subscribe path take over identically to the "had data from the start" case.

## 6. Presentation: Status page host graphs (`LiveResourceGraph.razor`)

- [x] 6.1 Layout reworked from two rows to three: (1) CPU alone (a lone flex item already stretches its *container* full-width via the existing `.resource-graph` flex rule); (2) CPU per core + RAM; (3) Network + Disk I/O.
- [x] 6.2 Host CPU chart created/seeded with a 20-minute `liveWindow`/`Get` window; every other host chart (RAM, per-core, network, disk) explicitly uses 10 minutes rather than relying on the buffer's own (now 20-minute) default.
- [x] 6.3 New Disk I/O chart wired through `OnAfterRenderAsync` (create+seed), `OnTick` (push), and `DisposeAsync` (destroy), mirroring Network's existing wiring exactly.
- [x] 6.4 **Found and fixed live**: the container stretching full-width (6.1) didn't mean the *chart itself* rendered at that width - Chart.js's own `maintainAspectRatio: true` default computed a ~320px width from the CSS-capped 160px height instead. Fixed in `chart-interop.js` (`maintainAspectRatio: false`) plus a new `.resource-graph-canvas-wrap` wrapper (`position: relative; height: 160px;`) around every canvas in both `RunHistoryGraph.razor` and `LiveResourceGraph.razor`, replacing the old canvas-level `max-height` rule - Chart.js's responsive sizing needs a defined height on the canvas's *parent*, not the canvas itself. See design.md's own dedicated decision entry.

## 7. Tests

- [x] 7.1 `ResourceHistoryBufferTests`: renamed/updated the 10-minute eviction test to 20 minutes; new test for `Get`'s windowed-restriction-without-eviction behavior; extended the round-trip test to cover disk fields too.
- [x] 7.2 `HostResourceSamplerTests` (real counters): assert disk figures are `>= 0` alongside the existing network assertions.
- [x] 7.3 `HostResourceSamplerServiceTests`: new test asserting a tracked run's persisted sample carries that tick's host-level network/disk figures; extended the transfer-tagging test to assert the same for a transfer's sample.
- [x] 7.4 `RunDetailComponentTests`: canvas/series-count assertions updated (2→4 canvases, 2→6 `SetSeriesAsync` calls); new test asserting CPU/RAM hide their legend and Network/Disk I/O keep theirs.
- [x] 7.5 `StatusComponentTests`: canvas-count assertions updated (4→5, both places); legend test extended to cover Disk I/O; new test asserting the host CPU chart alone uses a 20-minute `liveWindow` while every other host chart uses 10.
- [x] 7.6 `SpyChartInterop`: `CreatedCharts` tuple gained a `LiveWindow` field.
- [x] 7.7 New `RunDetailComponentTests`: a run with a pre-existing sample plus a simulated tick (mirroring `HostResourceSamplerService.TickCoreAsync`'s own buffer-then-publish ordering) gets exact `PushPointAsync` calls for CPU/network/disk with the right timestamp and value, and none of its four charts have a `LiveWindow`; a run with zero samples at load shows the empty-state message, then transitions to the four charts once a tick with real data lands.
- [x] 7.8 Full `dotnet test` (UnitTests, ArchitectureTests, BehaviorTests) run clean.

## 8. Live verification

- [x] 8.1 Verified live via a CDP-driven headless browser session against a real running host: Status page's three-row host layout (row 1 CPU full-width, legend-less; row 2 per-core+RAM; row 3 Network+Disk I/O, both with a legend and real data from an actual clone operation's disk/network activity); a run's detail page's two-row layout (CPU/RAM legend-less, Network/Disk I/O with legend). Screenshots reviewed before commit per this project's UI-change convention.
- [x] 8.2 The live-*update* mechanism specifically (section 5.5/5.6) was **not** additionally re-verified via a real browser session against a genuinely multi-tick-spanning run: every dashboard-triggerable operation (clone/pull/fetch/clean) is local-filesystem-only, and local `git clone` hardlinks objects instead of copying them, making every real local clone complete in well under one sampling tick (~3s) regardless of source repository size - there is no dashboard-reachable operation slow enough to observe live growth in a screenshot without a materially larger effort (e.g. driving the MCP `dotnet_run` tool's own raw JSON-RPC protocol directly, to run something deliberately slow). Covered instead by 7.7's precise, deterministic unit tests (exact interop calls/values) plus the fact that this reuses `LiveResourceGraph`'s own already-live-verified mechanism (8.1) unchanged, just wired to a different canvas set.

## 9. Docs

- [x] 9.1 `host-resource-monitoring`/`run-history`/`service-dashboard` spec deltas.
- [x] 9.2 `CHANGELOG.md` entry under `Added`.
