// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="History"/> component via bUnit - closes the gap tasks.md's §12
/// deviation notes left open for 12.15 (filtering/searching the History view narrows the list
/// correctly, and clearing restores it), the same way <c>StatusComponentTests</c> closes 12.12/12.13.
/// </summary>
public sealed class HistoryComponentTests : BunitContext, IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly IRunEventBus _runEventBus = new InMemoryRunEventBus(NullLogger<InMemoryRunEventBus>.Instance);

    public HistoryComponentTests()
    {
        Services.AddSingleton<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));
        Services.AddSingleton(_runEventBus);

        // The repository filter resolves an operator-typed name against the configured root before
        // querying - a real resolver backed by the same root every test's fake repository paths
        // (`C:\repositories\...`) already assume, matching RepositoriesComponentTests' own pattern.
        var options = Options.Create(new ServiceOptions { RootDirectory = @"C:\repositories" });
        Services.AddSingleton<IWorkingDirectoryResolver>(new WorkingDirectoryResolver(options, NullLogger<WorkingDirectoryResolver>.Instance));
    }

    [Fact]
    public async Task Filtering_by_kind_shows_only_matching_runs()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, "dotnet-repository"), CancellationToken.None);
        await runRepository.AddAsync(Sample(RunKind.GitCommand, "git-repository"), CancellationToken.None);

        var cut = Render<History>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("dotnet-repository", cut.Markup);
            Assert.Contains("git-repository", cut.Markup);
        });

        var gitCheckbox = cut.FindAll("input[type=checkbox]")
            .Single(input => input.ParentElement!.TextContent.Trim() == RunKind.GitCommand.ShortLabel());
        gitCheckbox.Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("git-repository", cut.Markup);
            Assert.DoesNotContain("dotnet-repository", cut.Markup);
        });
    }

    [Fact]
    public async Task Filtering_by_repository_shows_only_that_repos_runs()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, @"C:\repositories\alpha"), CancellationToken.None);
        await runRepository.AddAsync(Sample(RunKind.GitCommand, @"C:\repositories\beta"), CancellationToken.None);

        var cut = Render<History>();
        cut.WaitForAssertion(() => Assert.Contains("alpha", cut.Markup));

        // The filter is a repository name, not the full path the list itself no longer displays.
        cut.Find("input[placeholder='Repository name']").Input("alpha");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("alpha", cut.Markup);
            Assert.DoesNotContain("beta", cut.Markup);
        });
    }

    [Fact]
    public async Task Searching_narrows_the_list_after_the_debounce_delay()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, @"C:\repositories\needle-repository"), CancellationToken.None);
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, @"C:\repositories\unrelated"), CancellationToken.None);

        var cut = Render<History>();
        cut.WaitForAssertion(() => Assert.Contains("unrelated", cut.Markup));

        cut.Find("input[placeholder*='Search']").Input("needle");

        // The search box debounces for 300ms before querying - WaitForAssertion's own retry
        // window (bUnit's default) comfortably covers that.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("needle-repository", cut.Markup);
            Assert.DoesNotContain("unrelated", cut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Clearing_filters_and_search_restores_the_full_list()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, @"C:\repositories\alpha"), CancellationToken.None);
        await runRepository.AddAsync(Sample(RunKind.GitCommand, @"C:\repositories\beta"), CancellationToken.None);

        var cut = Render<History>();
        cut.WaitForAssertion(() => Assert.Contains("alpha", cut.Markup));

        cut.Find("input[placeholder='Repository name']").Input("alpha");
        cut.WaitForAssertion(() => Assert.DoesNotContain("beta", cut.Markup));

        var clearFiltersButton = cut.FindAll("button").Single(b => b.TextContent == "Clear filters");
        clearFiltersButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("alpha", cut.Markup);
            Assert.Contains("beta", cut.Markup);
        });
    }

    [Fact]
    public async Task A_run_completed_elsewhere_appears_live_without_reload()
    {
        // Reproduces the reported bug directly: a run started and completed via the MCP tools (no
        // dashboard interaction at all, just like a real git_run/dotnet_run call), publishing the
        // same RunEvent a dashboard-triggered action would - History must pick it up on its own,
        // the same way Status already does, per service-dashboard's "Completion moves a run from
        // status to history live" requirement.
        var cut = Render<History>();
        cut.WaitForAssertion(() => Assert.Contains("No runs match the current filters.", cut.Markup));

        var runRepository = Services.GetRequiredService<IRunRepository>();
        var runId = Guid.NewGuid();
        var repositoryPath = @"C:\repositories\mcp-triggered";
        await runRepository.AddAsync(new Run
        {
            Id = runId,
            Kind = RunKind.DotnetCommand,
            RepositoryPath = repositoryPath,
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            ExitCode = 0,
            Outcome = RunOutcome.Completed,
        }, CancellationToken.None);

        _runEventBus.Publish(new RunEvent(runId, RunKind.DotnetCommand, RunEventType.Terminal, repositoryPath));

        cut.WaitForAssertion(() => Assert.Contains("mcp-triggered", cut.Markup), TimeSpan.FromSeconds(2));
    }

    private static Run Sample(RunKind kind, string repositoryPath) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        RepositoryPath = repositoryPath,
        Arguments = kind is RunKind.DotnetCommand or RunKind.GitCommand ? ["build"] : null,
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Outcome = RunOutcome.Completed,
    };

    /// <inheritdoc />
    public new void Dispose()
    {
        _dbContextFactory.Dispose();
        base.Dispose();
    }
}
