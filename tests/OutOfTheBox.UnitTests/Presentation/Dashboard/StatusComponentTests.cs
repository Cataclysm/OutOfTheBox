// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Events;
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

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="Status"/> component via bUnit - a genuine Blazor render pipeline
/// (component lifecycle, <c>IRunEventBus</c> subscription, <c>InvokeAsync</c>/<c>StateHasChanged</c>),
/// not a real browser/SignalR circuit, but enough to actually verify the "updates live without
/// reload" claim instead of only code-reviewing it. Closes the gap tasks.md's §12 deviation notes
/// left open for 12.12/12.13 (no Blazor-interactive test client in this project's toolchain).
/// </summary>
public sealed class StatusComponentTests : BunitContext, IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly IRunEventBus _runEventBus = new InMemoryRunEventBus();
    private readonly IResourceEventBus _resourceEventBus = new InMemoryResourceEventBus();
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
            RepoPath = @"C:\repos\example",
            Arguments = ["build"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();

        cut.WaitForAssertion(() => Assert.Contains(@"C:\repos\example", cut.Markup));
        Assert.DoesNotContain("Idle - no runs in flight.", cut.Markup);
    }

    [Fact]
    public async Task A_new_run_appears_live_when_a_Started_event_is_published_without_reload()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains("Idle - no runs in flight.", cut.Markup));

        var runId = Guid.NewGuid();
        var repoPath = @"C:\repos\live-example";
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.GitCommand,
            RepoPath = repoPath,
            Arguments = ["pull"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        // The component never sees this DB write directly - only the published event, exactly like
        // a second HTTP client's POST /run/git would trigger via RunEndpoints in production.
        _runEventBus.Publish(new RunEvent(runId, RunKind.GitCommand, RunEventType.Started, repoPath));

        cut.WaitForAssertion(() => Assert.Contains(repoPath, cut.Markup), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_completed_run_disappears_live_when_a_Terminal_event_is_published_without_reload()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var runId = Guid.NewGuid();
        var repoPath = @"C:\repos\finishing-example";
        var run = new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepoPath = repoPath,
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, CancellationToken.None);

        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains(repoPath, cut.Markup));

        run.Outcome = RunOutcome.Completed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.ExitCode = 0;
        await runRepository.UpdateAsync(run, CancellationToken.None);

        _runEventBus.Publish(new RunEvent(runId, RunKind.DotnetCommand, RunEventType.Terminal, repoPath));

        cut.WaitForAssertion(() => Assert.Contains("Idle - no runs in flight.", cut.Markup), TimeSpan.FromSeconds(2));
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
        _runEventBus.Publish(new RunEvent(Guid.NewGuid(), RunKind.DotnetCommand, RunEventType.OutputLine, @"C:\repos\x") { OutputStream = "stdout", OutputLine = "hello" });

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Equal(before, cut.Markup);
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
            RepoPath = @"C:\repos\example",
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains(@"C:\repos\example", cut.Markup));
        Assert.DoesNotContain("process-row", cut.Markup);

        var process = new ProcessResourceSample(1234, "dotnet", DateTime.UtcNow, 12.5, 4096);
        var runAggregate = new RunResourceAggregate(runId, 12.5, 4096, [process]);
        _resourceEventBus.Publish(new ResourceSnapshot(DateTimeOffset.UtcNow, new HostResourceSample(0, [], 0, 0, 0, 0, 0), [runAggregate]));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("process-row", cut.Markup);
            Assert.Contains("dotnet (1234)", cut.Markup);
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
            RepoPath = @"C:\repos\example",
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains(@"C:\repos\example", cut.Markup));

        var startTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var process = new ProcessResourceSample(4321, "testhost", startTime, 5, 2048);
        _resourceEventBus.Publish(new ResourceSnapshot(DateTimeOffset.UtcNow, new HostResourceSample(0, [], 0, 0, 0, 0, 0), [new RunResourceAggregate(runId, 5, 2048, [process])]));
        cut.WaitForAssertion(() => Assert.Contains("process-row", cut.Markup), TimeSpan.FromSeconds(2));

        cut.Find(".process-row button").Click();

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

        // Host graphs are always live (never lazily mounted) - four canvases (CPU, RAM, per-core
        // CPU, network) exist immediately, with no run cards involved.
        cut.WaitForAssertion(() => Assert.Equal(4, _chartInterop.CreatedCanvasIds.Count));

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
    public async Task A_runs_live_chart_is_not_created_until_its_card_is_expanded_and_is_destroyed_on_collapse()
    {
        // Covers task 15.9: no interop calls and no per-run subscription while collapsed. Since
        // LiveResourceGraph only calls IChartInterop.CreateLineChartAsync (and subscribes to
        // IResourceEventBus) from OnAfterRenderAsync, and Status.razor only renders the component
        // at all once the run's id is in _expandedRunIds, "never created" is equivalent proof to
        // "never subscribed" here - there's no code path that would subscribe without also creating.
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var runId = Guid.NewGuid();
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepoPath = @"C:\repos\example",
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains(@"C:\repos\example", cut.Markup));

        var createdBeforeExpand = _chartInterop.CreatedCanvasIds.Count;
        Assert.Equal(4, createdBeforeExpand); // host graph only - nothing for the collapsed run card

        cut.Find("button.run-graph-toggle").Click();

        cut.WaitForAssertion(() => Assert.Equal(createdBeforeExpand + 2, _chartInterop.CreatedCanvasIds.Count), TimeSpan.FromSeconds(2));

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

        cut.WaitForAssertion(() => Assert.Equal(destroyedBeforeCollapse + 2, _chartInterop.DestroyedCanvasIds.Count), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task An_expanded_transfers_live_graph_shows_the_host_activity_note()
    {
        // Task 15.7's transfer variant: a transfer's own series (host-tagged, per
        // HostResourceSamplerService.TickAsync) renders the same way a command run's does, but
        // labeled as host-level activity rather than a per-process figure.
        var runRepository = Services.GetRequiredService<IRunRepository>();
        var transferId = Guid.NewGuid();
        await runRepository.AddAsync(new Run
        {
            Id = transferId,
            Kind = RunKind.ArtifactTransfer,
            RepoPath = @"C:\repos\example",
            ArtifactPath = "bin/output.dll",
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        }, CancellationToken.None);

        var cut = Render<Status>();
        cut.WaitForAssertion(() => Assert.Contains(@"C:\repos\example", cut.Markup));

        cut.Find("button.run-graph-toggle").Click();

        cut.WaitForAssertion(() => Assert.Contains("Host activity during transfer", cut.Markup), TimeSpan.FromSeconds(2));
    }

    /// <inheritdoc />
    public new void Dispose()
    {
        _dbContextFactory.Dispose();
        base.Dispose();
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
