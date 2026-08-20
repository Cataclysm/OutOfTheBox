// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Diagnostics;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Monitoring;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Monitoring;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.Presentation.Dashboard.Charts;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="Status"/> component via bUnit - a genuine Blazor render pipeline
/// (component lifecycle, <c>IRunEventBus</c> subscription, <c>InvokeAsync</c>/<c>StateHasChanged</c>),
/// not a real browser/SignalR circuit, but enough to actually verify the "updates live without
/// reload" claim instead of only code-reviewing it. Closes the gap tasks.md's §12 deviation notes
/// left open for 12.12/12.13 (no Blazor-interactive test client in this project's toolchain).
/// </summary>
public sealed class StatusComponentTests : DashboardComponentTestContext, IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly IRunEventBus _runEventBus = new InMemoryRunEventBus(NullLogger<InMemoryRunEventBus>.Instance);
    private readonly IResourceEventBus _resourceEventBus = new InMemoryResourceEventBus(NullLogger<InMemoryResourceEventBus>.Instance);
    private readonly SpyProcessMonitor _processMonitor = new();
    private readonly SpyChartInterop _chartInterop = new();

    public StatusComponentTests()
    {
        Services.AddSingleton<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));
        Services.AddSingleton(_runEventBus);
        Services.AddSingleton(_resourceEventBus);
        Services.AddSingleton<IProcessMonitor>(_processMonitor);
        Services.AddSingleton<IChartInterop>(_chartInterop);
        Services.AddSingleton(new ResourceHistoryBuffer(new SystemClock()));
        Services.AddSingleton<IInstalledToolVersionsProvider>(new StubInstalledToolVersionsProvider(new InstalledToolVersions("10.0.100", "2.43.0")));
        Services.AddSingleton<IRootDirectoryDiskSpaceProvider>(new StubRootDirectoryDiskSpaceProvider(new DiskSpaceInfo(500_000_000_000, 200_000_000_000)));
    }

    [Fact]
    public void Shows_idle_empty_state_when_no_runs_are_in_flight()
    {
        var cut = Render<Status>();

        cut.WaitForAssertion(() => Assert.Contains("Idle - no runs in flight.", cut.Markup));
    }

    [Fact]
    public async Task Shows_an_already_running_run_at_initial_render()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        await runRepository.AddAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.DotnetCommand,
            RepositoryPath = @"C:\repositories\example",
            Arguments = ["build"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();

        cut.WaitForAssertion(() => Assert.Contains("example", cut.Markup));
        Assert.DoesNotContain("Idle - no runs in flight.", cut.Markup);
    }

    [Fact]
    public async Task A_new_run_appears_live_when_a_Started_event_is_published_without_reload()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains("Idle - no runs in flight.", cut.Markup));

        var runId = Guid.NewGuid();
        var repositoryPath = @"C:\repositories\live-example";
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.GitCommand,
            RepositoryPath = repositoryPath,
            Arguments = ["pull"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        // The component never sees this DB write directly - only the published event, exactly like
        // a real git_run MCP tool call would trigger via CommandExecutionMcpTools in production.
        _runEventBus.Publish(new RunEvent(runId, RunKind.GitCommand, RunEventType.Started, repositoryPath));

        cut.WaitForAssertion(() => Assert.Contains(Path.GetFileName(repositoryPath), cut.Markup), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_completed_run_stays_visible_for_a_short_while_after_a_Terminal_event_rather_than_vanishing_immediately()
    {
        // Per direct request: a run that finishes quickly (well under Status's own
        // MinimumCompletedVisibleDuration) must still be visible for a moment after going terminal,
        // not disappear on the very next render - otherwise a fast run could flicker past too quickly
        // to register as having run at all. This intentionally does not wait out the full hold
        // duration (10s of real wall-clock time in a unit test) - that it's still shown immediately
        // after Terminal is the regression this guards.
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var runId = Guid.NewGuid();
        var repositoryPath = @"C:\repositories\finishing-example";
        var run = new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepositoryPath = repositoryPath,
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, CancellationToken.None);

        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains(Path.GetFileName(repositoryPath), cut.Markup));

        run.Outcome = RunOutcome.Completed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.ExitCode = 0;
        await runRepository.UpdateAsync(run, CancellationToken.None);

        _runEventBus.Publish(new RunEvent(runId, RunKind.DotnetCommand, RunEventType.Terminal, repositoryPath));

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.Contains(Path.GetFileName(repositoryPath), cut.Markup);
        Assert.DoesNotContain("Idle - no runs in flight.", cut.Markup);
    }

    [Fact]
    public async Task An_output_line_event_does_not_trigger_a_refresh()
    {
        // Per Status.razor's own documented scope (task 12.7): the list shows summary fields only,
        // not a live output preview - an OutputLine event must be a no-op here, not just "harmless."
        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains("Idle - no runs in flight.", cut.Markup));

        // Deliberately not persisted anywhere - if OnRunEvent's OutputLine guard were removed, the
        // component would try to re-query and (correctly) still show idle, which would make this
        // test pass for the wrong reason. Asserting markup is unchanged, not just "still idle",
        // demonstrates OutputLine specifically doesn't re-render.
        var before = cut.Markup;
        _runEventBus.Publish(new RunEvent(Guid.NewGuid(), RunKind.DotnetCommand, RunEventType.OutputLine, @"C:\repositories\x") { OutputStream = "stdout", OutputLine = "hello" });

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Equal(before, cut.Markup);
    }

    [Fact]
    public void Installed_tool_version_tiles_show_the_providers_reported_versions()
    {
        var cut = Render<Status>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("resource-tile-value\">10.0.100", cut.Markup);
            Assert.Contains("resource-tile-value\">2.43.0", cut.Markup);
        });
    }

    [Fact]
    public void Service_tile_shows_the_running_builds_version()
    {
        var cut = Render<Status>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("resource-tile-label\">Service<", cut.Markup);
            Assert.Contains($"resource-tile-value\">{VersionInfo.Current}", cut.Markup);
        });
    }

    [Fact]
    public void Single_line_host_charts_hide_their_legend_but_the_two_line_charts_keep_it()
    {
        // CPU/RAM/per-core all hide their legend - CPU and RAM because a single-entry legend is
        // redundant with the chart's own ".resource-graph-label" heading, per-core because an
        // 8+-entry legend is noise, not information (see the dedicated per-core test this replaced).
        // Network and Disk I/O are the two charts where the legend still earns its place: Sent vs.
        // Received (or Read vs. Write) can't be told apart without it.
        var cut = Render<Status>();

        cut.WaitForAssertion(() => Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-disk-", StringComparison.Ordinal)));

        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-cpu-", StringComparison.Ordinal) && !c.ShowLegend);
        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-ram-", StringComparison.Ordinal) && !c.ShowLegend);
        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-per-core-", StringComparison.Ordinal) && !c.ShowLegend);
        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-network-", StringComparison.Ordinal) && c.ShowLegend);
        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-disk-", StringComparison.Ordinal) && c.ShowLegend);
    }

    [Fact]
    public void Host_cpu_chart_uses_a_20_minute_live_window_but_every_other_host_chart_uses_10()
    {
        // Per direct instruction: the Status page's row-1 host CPU graph alone shows 20 minutes of
        // live data; every other live graph (here and on the run-detail page) still shows 10.
        var cut = Render<Status>();

        cut.WaitForAssertion(() => Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-disk-", StringComparison.Ordinal)));

        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-cpu-", StringComparison.Ordinal) && c.LiveWindow == TimeSpan.FromMinutes(20));
        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-ram-", StringComparison.Ordinal) && c.LiveWindow == TimeSpan.FromMinutes(10));
        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-per-core-", StringComparison.Ordinal) && c.LiveWindow == TimeSpan.FromMinutes(10));
        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-network-", StringComparison.Ordinal) && c.LiveWindow == TimeSpan.FromMinutes(10));
        Assert.Contains(_chartInterop.CreatedCharts, c => c.CanvasId.StartsWith("live-disk-", StringComparison.Ordinal) && c.LiveWindow == TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Host_tiles_update_live_when_a_resource_snapshot_is_published()
    {
        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains("resource-tile-value\">0.0%", cut.Markup));

        var host = new HostResourceSample(TotalCpuPercent: 37.5, PerCoreCpuPercent: [37.5], TotalRamBytes: 1000, AvailableRamBytes: 400, ServiceRamBytes: 55, NetworkBytesSentPerSecond: 0, NetworkBytesReceivedPerSecond: 0);
        _resourceEventBus.Publish(new ResourceSnapshot(DateTimeOffset.UtcNow, host, []));

        cut.WaitForAssertion(() => Assert.Contains("resource-tile-value\">37.5%", cut.Markup), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Process_sublist_appears_under_its_owning_run_when_a_resource_snapshot_is_published()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var runId = Guid.NewGuid();
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepositoryPath = @"C:\repositories\example",
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains("example", cut.Markup));
        Assert.DoesNotContain("process-toggle", cut.Markup);

        var process = new ProcessResourceSample(1234, "dotnet", DateTime.UtcNow, 12.5, 4096);
        var runAggregate = new RunResourceAggregate(runId, 12.5, 4096, [process]);
        _resourceEventBus.Publish(new ResourceSnapshot(DateTimeOffset.UtcNow, new HostResourceSample(0, [], 0, 0, 0, 0, 0), [runAggregate]));

        // Already running when this page loaded, so already auto-expanded (per direct request) - the
        // process table appears the instant its first data point arrives, no click needed.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("process-table", cut.Markup);
            Assert.Contains(">dotnet<", cut.Markup);
            Assert.Contains(">1234<", cut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Clicking_kill_calls_IProcessMonitor_with_the_processs_id_and_start_time()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var runId = Guid.NewGuid();
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepositoryPath = @"C:\repositories\example",
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains("example", cut.Markup));

        var startTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var process = new ProcessResourceSample(4321, "testhost", startTime, 5, 2048);
        _resourceEventBus.Publish(new ResourceSnapshot(DateTimeOffset.UtcNow, new HostResourceSample(0, [], 0, 0, 0, 0, 0), [new RunResourceAggregate(runId, 5, 2048, [process])]));

        // Already auto-expanded (already running at page load) - no toggle click needed first.
        cut.WaitForAssertion(() => Assert.Contains("process-kill-button", cut.Markup), TimeSpan.FromSeconds(2));

        cut.Find(".process-kill-button").Click();

        Assert.Equal((4321, startTime), _processMonitor.LastKillCall);
    }

    [Fact]
    public void Host_resource_graph_is_created_on_render_and_extends_live_as_snapshots_arrive()
    {
        // Covers task 15.6 as a bUnit test rather than a Reqnroll .feature file, for the same
        // reason 12.12-12.15's live-update gap was closed via bUnit: there's no Blazor-interactive
        // browser test client in this project's toolchain, so a real Chart.js render can't be
        // driven by BehaviorTests - what's verifiable is that the right interop calls happen with
        // the right data, via SpyChartInterop standing in for the JS engine.
        var cut = Render<Status>();

        // Host graphs are always live (never lazily mounted) - five canvases (CPU, RAM, per-core
        // CPU, network, disk I/O) exist immediately, with no run cards involved.
        cut.WaitForAssertion(() => Assert.Equal(5, _chartInterop.CreatedCanvasIds.Count));

        var historyBuffer = Services.GetRequiredService<ResourceHistoryBuffer>();
        var timestamp = DateTimeOffset.UtcNow;
        // Mirrors HostResourceSamplerService.TickAsync's own ordering: the buffer is updated before
        // the snapshot is published, which is what LiveResourceGraph's tick handler relies on.
        historyBuffer.Add(ResourceHistoryBuffer.HostSeriesKey, timestamp, 42, 800);
        _resourceEventBus.Publish(new ResourceSnapshot(timestamp, new HostResourceSample(42, [42], 1000, 200, 50, 0, 0), []));

        cut.WaitForAssertion(
            () => Assert.Contains(_chartInterop.PushedPoints, p => p.Timestamp == timestamp && p.Value == 42),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_runs_live_chart_is_created_immediately_for_an_already_running_run_and_destroyed_on_manual_collapse()
    {
        // Per direct request: a run already in flight when this page loads starts auto-expanded
        // (OnInitializedAsync's own "treat already-running the same as just-started" handling), not
        // lazily on click - manually collapsing it (the operator's own override, still available on
        // top of the auto-expand default) still destroys the chart the same way it always has.
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var runId = Guid.NewGuid();
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepositoryPath = @"C:\repositories\example",
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();

        // Host graph (5 canvases) plus the run's own CPU/RAM/Network/Disk (4), all present without
        // any click - createdBeforeExpand from the earlier lazy-expansion test no longer applies.
        cut.WaitForAssertion(() => Assert.Equal(9, _chartInterop.CreatedCanvasIds.Count), TimeSpan.FromSeconds(2));
        Assert.Contains("Hide graph", cut.Markup);

        var historyBuffer = Services.GetRequiredService<ResourceHistoryBuffer>();
        var timestamp = DateTimeOffset.UtcNow;
        historyBuffer.Add(runId.ToString(), timestamp, 17, 4096);
        _resourceEventBus.Publish(new ResourceSnapshot(timestamp, new HostResourceSample(0, [], 0, 0, 0, 0, 0), [new RunResourceAggregate(runId, 17, 4096, [])]));

        // Task 15.7: the run's own graph continues updating live once expanded.
        cut.WaitForAssertion(
            () => Assert.Contains(_chartInterop.PushedPoints, p => p.Timestamp == timestamp && p.Value == 17),
            TimeSpan.FromSeconds(2));

        var destroyedBeforeCollapse = _chartInterop.DestroyedCanvasIds.Count;
        cut.Find("button.run-graph-toggle").Click();

        cut.WaitForAssertion(() => Assert.Equal(destroyedBeforeCollapse + 4, _chartInterop.DestroyedCanvasIds.Count), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_new_runs_live_chart_and_process_table_auto_expand_on_a_Started_event()
    {
        // Distinct from the "already running at page load" test above - this run is added and
        // started only *after* the page has already rendered, so only OnRunEvent's own Started
        // handling (not OnInitializedAsync's one-time initial-load loop) is what could expand it.
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains("Idle - no runs in flight.", cut.Markup));

        var runId = Guid.NewGuid();
        var repositoryPath = @"C:\repositories\fresh-example";
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepositoryPath = repositoryPath,
            Arguments = ["build"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);
        _runEventBus.Publish(new RunEvent(runId, RunKind.DotnetCommand, RunEventType.Started, repositoryPath));

        cut.WaitForAssertion(() => Assert.Contains("Hide graph", cut.Markup), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_runs_live_chart_and_process_table_auto_collapse_immediately_on_a_Terminal_event()
    {
        // Per direct request: collapse happens the moment the run goes terminal, not after the row's
        // own separate MinimumCompletedVisibleDuration hold (which only keeps the row itself visible,
        // not its now-frozen graph/process sections).
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var runId = Guid.NewGuid();
        var repositoryPath = @"C:\repositories\example";
        var run = new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepositoryPath = repositoryPath,
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, CancellationToken.None);

        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains("Hide graph", cut.Markup));

        run.Outcome = RunOutcome.Completed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.ExitCode = 0;
        await runRepository.UpdateAsync(run, CancellationToken.None);
        _runEventBus.Publish(new RunEvent(runId, RunKind.DotnetCommand, RunEventType.Terminal, repositoryPath));

        // The row itself is still shown (MinimumCompletedVisibleDuration's own hold), but its graph
        // toggle has flipped back to "Show graph" - the section itself collapsed.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(Path.GetFileName(repositoryPath), cut.Markup);
            Assert.Contains("Show graph", cut.Markup);
            Assert.DoesNotContain("Hide graph", cut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_transfers_live_graph_shows_the_host_activity_note_once_running()
    {
        // Task 15.7's transfer variant: a transfer's own series (host-tagged, per
        // HostResourceSamplerService.TickAsync) renders the same way a command run's does, but
        // labeled as host-level activity rather than a per-process figure. Already auto-expanded
        // (already running at page load), so the note is visible with no toggle click needed.
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var transferId = Guid.NewGuid();
        await runRepository.AddAsync(new Run
        {
            Id = transferId,
            Kind = RunKind.FileTransfer,
            RepositoryPath = @"C:\repositories\example",
            FilePath = "bin/output.dll",
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();

        cut.WaitForAssertion(() => Assert.Contains("Host activity during transfer", cut.Markup), TimeSpan.FromSeconds(2));
    }

    /// <inheritdoc />
    public new void Dispose()
    {
        _dbContextFactory.Dispose();
        base.Dispose();
    }

    private sealed class StubInstalledToolVersionsProvider(InstalledToolVersions versions) : IInstalledToolVersionsProvider
    {
        public Task<InstalledToolVersions> GetVersionsAsync(CancellationToken cancellationToken) => Task.FromResult(versions);
    }

    private sealed class StubRootDirectoryDiskSpaceProvider(DiskSpaceInfo diskSpace) : IRootDirectoryDiskSpaceProvider
    {
        public Task<DiskSpaceInfo> GetDiskSpaceAsync(CancellationToken cancellationToken) => Task.FromResult(diskSpace);
    }

    private sealed class SpyProcessMonitor : IProcessMonitor
    {
        public (int ProcessId, DateTime StartTime)? LastKillCall { get; private set; }

        public Task<bool> KillAsync(int processId, DateTime expectedStartTime, CancellationToken cancellationToken)
        {
            LastKillCall = (processId, expectedStartTime);
            return Task.FromResult(true);
        }
    }
}
