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
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, "dotnet-repo"), CancellationToken.None);
        await runRepository.AddAsync(Sample(RunKind.GitCommand, "git-repo"), CancellationToken.None);

        var cut = Render<History>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("dotnet-repo", cut.Markup);
            Assert.Contains("git-repo", cut.Markup);
        });

        var gitCheckbox = cut.FindAll("input[type=checkbox]")
            .Single(input => input.ParentElement!.TextContent.Trim() == nameof(RunKind.GitCommand));
        gitCheckbox.Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("git-repo", cut.Markup);
            Assert.DoesNotContain("dotnet-repo", cut.Markup);
        });
    }

    [Fact]
    public async Task Filtering_by_repository_shows_only_that_repos_runs()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, @"C:\repos\alpha"), CancellationToken.None);
        await runRepository.AddAsync(Sample(RunKind.GitCommand, @"C:\repos\beta"), CancellationToken.None);

        var cut = Render<History>();
        cut.WaitForAssertion(() => Assert.Contains(@"C:\repos\alpha", cut.Markup));

        cut.Find("input[placeholder='repo path']").Input(@"C:\repos\alpha");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(@"C:\repos\alpha", cut.Markup);
            Assert.DoesNotContain(@"C:\repos\beta", cut.Markup);
        });
    }

    [Fact]
    public async Task Searching_narrows_the_list_after_the_debounce_delay()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, @"C:\repos\needle-repo"), CancellationToken.None);
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, @"C:\repos\unrelated"), CancellationToken.None);

        var cut = Render<History>();
        cut.WaitForAssertion(() => Assert.Contains("unrelated", cut.Markup));

        cut.Find("input[placeholder*='search']").Input("needle");

        // The search box debounces for 300ms before querying - WaitForAssertion's own retry
        // window (bUnit's default) comfortably covers that.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("needle-repo", cut.Markup);
            Assert.DoesNotContain("unrelated", cut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Clearing_filters_and_search_restores_the_full_list()
    {
        var runRepository = Services.GetRequiredService<IRunRepository>();
        await runRepository.AddAsync(Sample(RunKind.DotnetCommand, @"C:\repos\alpha"), CancellationToken.None);
        await runRepository.AddAsync(Sample(RunKind.GitCommand, @"C:\repos\beta"), CancellationToken.None);

        var cut = Render<History>();
        cut.WaitForAssertion(() => Assert.Contains(@"C:\repos\alpha", cut.Markup));

        cut.Find("input[placeholder='repo path']").Input(@"C:\repos\alpha");
        cut.WaitForAssertion(() => Assert.DoesNotContain(@"C:\repos\beta", cut.Markup));

        var clearFiltersButton = cut.FindAll("button").Single(b => b.TextContent == "Clear filters");
        clearFiltersButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(@"C:\repos\alpha", cut.Markup);
            Assert.Contains(@"C:\repos\beta", cut.Markup);
        });
    }

    private static Run Sample(RunKind kind, string repoPath) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        RepoPath = repoPath,
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
