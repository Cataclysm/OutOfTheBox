// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.Presentation.Dashboard.Charts;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="RunDetail"/> component via bUnit for every <see cref="RunKind"/> and
/// a representative spread of outcomes - closes the gap tasks.md's §12 deviation notes left open
/// for 12.14 (History/run-detail render correctly across every kind and outcome), now that §13 has
/// made <see cref="RunKind.RepositoryClone"/>/<see cref="RunKind.RepositoryDelete"/> real.
/// </summary>
public sealed class RunDetailComponentTests : BunitContext, IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly SpyChartInterop _chartInterop = new();

    public RunDetailComponentTests()
    {
        Services.AddSingleton<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));
        Services.AddSingleton<IRunResourceSampleRepository>(_ => new EfRunResourceSampleRepository(_dbContextFactory.CreateContext()));
        Services.AddSingleton<IChartInterop>(_chartInterop);
    }

    [Fact]
    public async Task Renders_a_completed_dotnet_run_with_full_output()
    {
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.DotnetCommand,
            RepoPath = @"C:\repos\example",
            Arguments = ["test", "--filter", "Foo"],
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Completed,
            ExitCode = 0,
            Stdout = "all tests passed",
            Stderr = string.Empty,
        });

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("test --filter Foo", cut.Markup);
            Assert.Contains("all tests passed", cut.Markup);
            Assert.DoesNotContain("Output was truncated", cut.Markup);
        });
    }

    [Fact]
    public async Task Renders_a_timed_out_git_run_with_a_truncation_indicator()
    {
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.GitCommand,
            RepoPath = @"C:\repos\example",
            Arguments = ["fetch", "--all"],
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.TimedOut,
            Stdout = "partial output",
            Truncated = true,
        });

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("TimedOut", cut.Markup);
            Assert.Contains("Output was truncated", cut.Markup);
        });
    }

    [Fact]
    public async Task Renders_a_cancelled_artifact_transfer_without_a_stdout_panel()
    {
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.ArtifactTransfer,
            RepoPath = @"C:\repos\example",
            ArtifactPath = "bin/output.dll",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Cancelled,
        });

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("bin/output.dll", cut.Markup);
            Assert.Contains("Cancelled", cut.Markup);
            Assert.DoesNotContain("run-output", cut.Markup);
        });
    }

    [Fact]
    public async Task Renders_a_completed_clone_with_its_source_url()
    {
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.RepositoryClone,
            RepoPath = @"C:\repos\new-repo",
            SourceUrl = "https://example.com/repo.git",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Completed,
            ExitCode = 0,
            Stdout = "Cloning into 'new-repo'...",
        });

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("https://example.com/repo.git", cut.Markup);
            Assert.Contains("Cloning into", cut.Markup);
        });
    }

    [Fact]
    public async Task Renders_an_already_exists_clone_rejection_with_no_output_panel()
    {
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.RepositoryClone,
            RepoPath = @"C:\repos\existing",
            SourceUrl = "https://example.com/repo.git",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.AlreadyExists,
        });

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() => Assert.Contains("AlreadyExists", cut.Markup));
    }

    [Fact]
    public async Task Renders_a_completed_delete_with_only_repo_timestamps_and_outcome()
    {
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.RepositoryDelete,
            RepoPath = @"C:\repos\removed-repo",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Completed,
        });

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(@"C:\repos\removed-repo", cut.Markup);
            Assert.Contains("Completed", cut.Markup);
            // Delete has no command/exit-code/source-URL/artifact fields and no stdout/stderr panel.
            Assert.DoesNotContain("run-output", cut.Markup);
            Assert.DoesNotContain("Exit code", cut.Markup);
        });
    }

    [Fact]
    public async Task Renders_a_not_found_delete_rejection()
    {
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.RepositoryDelete,
            RepoPath = @"C:\repos\does-not-exist",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.NotFound,
        });

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() => Assert.Contains("NotFound", cut.Markup));
    }

    [Fact]
    public async Task Renders_an_interrupted_run()
    {
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.DotnetCommand,
            RepoPath = @"C:\repos\example",
            Arguments = ["build"],
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Interrupted,
        });

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() => Assert.Contains("Interrupted", cut.Markup));
    }

    [Fact]
    public async Task Renders_a_full_duration_resource_graph_spanning_longer_than_the_10_minute_live_window()
    {
        // Covers task 15.8 as a bUnit test (see StatusComponentTests for why - no Blazor-interactive
        // browser test client in this project's toolchain). Deliberately spans longer than
        // ResourceHistoryBuffer's 10-minute live window and reads zero live-only state, proving
        // this graph is fed by the full persisted RunResourceSamples series, not the buffer.
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.DotnetCommand,
            RepoPath = @"C:\repos\example",
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Completed,
            ExitCode = 0,
        });

        var sampleRepository = Services.GetRequiredService<IRunResourceSampleRepository>();
        for (var i = 0; i < 15; i++)
        {
            await sampleRepository.AddAsync(
                new RunResourceSample { RunId = run.Id, Timestamp = run.StartedAt.AddMinutes(i), CpuPercent = i, RamBytes = i * 100 },
                CancellationToken.None);
        }

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() => Assert.Equal(2, _chartInterop.CreatedCanvasIds.Count), TimeSpan.FromSeconds(2));
        Assert.Equal(2, _chartInterop.SeriesSet.Count); // one SetSeriesAsync call per canvas (CPU, RAM)
        Assert.All(_chartInterop.SeriesSet, s => Assert.Equal(15, s.Points.Count));
        Assert.Equal(0, _chartInterop.SeriesSet[0].Points[0].Value);
        Assert.Equal(14, _chartInterop.SeriesSet[0].Points[^1].Value);
    }

    [Fact]
    public async Task Renders_a_completed_transfers_full_duration_graph_with_the_host_activity_note()
    {
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.ArtifactTransfer,
            RepoPath = @"C:\repos\example",
            ArtifactPath = "bin/output.dll",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Completed,
        });

        var sampleRepository = Services.GetRequiredService<IRunResourceSampleRepository>();
        await sampleRepository.AddAsync(new RunResourceSample { RunId = run.Id, Timestamp = run.StartedAt, CpuPercent = 10, RamBytes = 500 }, CancellationToken.None);

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() => Assert.Contains("Host activity during transfer", cut.Markup), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task No_resource_graph_is_rendered_when_the_run_has_no_persisted_samples()
    {
        // A repository delete never gets a resource sample (per §11's RunResourceSample entity
        // note) - the graph component must degrade to rendering nothing, not an empty/broken chart.
        var run = await AddRunAsync(new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.RepositoryDelete,
            RepoPath = @"C:\repos\removed-repo",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Completed,
        });

        var cut = Render<RunDetail>(parameters => parameters.Add(p => p.RunId, run.Id));

        cut.WaitForAssertion(() => Assert.Contains(@"C:\repos\removed-repo", cut.Markup));
        Assert.Empty(_chartInterop.CreatedCanvasIds);
    }

    private async Task<Run> AddRunAsync(Run run)
    {
        await Services.GetRequiredService<IRunRepository>().AddAsync(run, CancellationToken.None);
        return run;
    }

    /// <inheritdoc />
    public new void Dispose()
    {
        _dbContextFactory.Dispose();
        base.Dispose();
    }
}
