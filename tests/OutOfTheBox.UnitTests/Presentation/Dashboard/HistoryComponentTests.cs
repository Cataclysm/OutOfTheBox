// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Renders the real <see cref="History"/> component via bUnit - closes the gap tasks.md's §12
/// deviation notes left open for 12.15 (filtering/searching the History view narrows the list
/// correctly, and clearing restores it), the same way <c>StatusComponentTests</c> closes 12.12/12.13.
/// </summary>
public sealed class HistoryComponentTests : BunitContext, IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();

    public HistoryComponentTests() => Services.AddSingleton<IRunRepository>(_ => new EfRunRepository(_dbContextFactory.CreateContext()));

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
        cut.WaitForAssertion(() => Assert.Contains(@"C:\repositories\alpha", cut.Markup));

        cut.Find("input[placeholder='Repository path']").Input(@"C:\repositories\alpha");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(@"C:\repositories\alpha", cut.Markup);
            Assert.DoesNotContain(@"C:\repositories\beta", cut.Markup);
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
        cut.WaitForAssertion(() => Assert.Contains(@"C:\repositories\alpha", cut.Markup));

        cut.Find("input[placeholder='Repository path']").Input(@"C:\repositories\alpha");
        cut.WaitForAssertion(() => Assert.DoesNotContain(@"C:\repositories\beta", cut.Markup));

        var clearFiltersButton = cut.FindAll("button").Single(b => b.TextContent == "Clear filters");
        clearFiltersButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(@"C:\repositories\alpha", cut.Markup);
            Assert.Contains(@"C:\repositories\beta", cut.Markup);
        });
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
