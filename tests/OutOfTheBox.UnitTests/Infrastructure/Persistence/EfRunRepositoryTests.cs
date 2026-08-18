// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Persistence;

namespace OutOfTheBox.UnitTests.Infrastructure.Persistence;

public sealed class EfRunRepositoryTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();

    [Theory]
    [InlineData(RunKind.DotnetCommand)]
    [InlineData(RunKind.GitCommand)]
    public async Task A_command_run_is_Running_while_in_flight_then_updated_to_its_terminal_outcome_with_full_output(RunKind kind)
    {
        var repository = CreateRepository();
        var run = new Run
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            RepositoryPath = @"C:\repositories\example",
            Arguments = ["build"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };

        await repository.AddAsync(run, CancellationToken.None);

        var whileInFlight = await repository.FindByIdAsync(run.Id, CancellationToken.None);
        Assert.NotNull(whileInFlight);
        Assert.Equal(RunOutcome.Running, whileInFlight.Outcome);
        Assert.Null(whileInFlight.CompletedAt);

        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Outcome = RunOutcome.Completed;
        run.ExitCode = 0;
        run.Stdout = "build succeeded";
        run.Stderr = string.Empty;
        run.Truncated = false;
        await repository.UpdateAsync(run, CancellationToken.None);

        var afterCompletion = await repository.FindByIdAsync(run.Id, CancellationToken.None);
        Assert.NotNull(afterCompletion);
        Assert.Equal(RunOutcome.Completed, afterCompletion.Outcome);
        Assert.Equal(0, afterCompletion.ExitCode);
        Assert.Equal("build succeeded", afterCompletion.Stdout);
        Assert.NotNull(afterCompletion.CompletedAt);
    }

    [Fact]
    public async Task A_transfer_is_Running_while_in_flight_then_updated_with_file_size_and_completed_outcome()
    {
        var repository = CreateRepository();
        var run = new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.FileTransfer,
            RepositoryPath = @"C:\repositories\example",
            FilePath = "bin/output.dll",
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };

        await repository.AddAsync(run, CancellationToken.None);

        var whileInFlight = await repository.FindByIdAsync(run.Id, CancellationToken.None);
        Assert.NotNull(whileInFlight);
        Assert.Equal(RunOutcome.Running, whileInFlight.Outcome);
        Assert.Null(whileInFlight.FileSizeBytes);

        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Outcome = RunOutcome.Completed;
        run.FileSizeBytes = 12345;
        await repository.UpdateAsync(run, CancellationToken.None);

        var afterCompletion = await repository.FindByIdAsync(run.Id, CancellationToken.None);
        Assert.NotNull(afterCompletion);
        Assert.Equal(RunOutcome.Completed, afterCompletion.Outcome);
        Assert.Equal(12345, afterCompletion.FileSizeBytes);
    }

    [Fact]
    public async Task Persisted_stdout_stderr_and_truncation_flag_match_what_was_streamed_for_a_truncated_run()
    {
        var repository = CreateRepository();
        var run = new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.DotnetCommand,
            RepositoryPath = @"C:\repositories\example",
            Arguments = ["test"],
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await repository.AddAsync(run, CancellationToken.None);

        // What the run's output sink would have accumulated before hitting the output cap.
        var truncatedStdout = string.Concat(Enumerable.Repeat("a line of output\n", 50));
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Outcome = RunOutcome.Completed;
        run.ExitCode = 0;
        run.Stdout = truncatedStdout;
        run.Stderr = "some stderr\n";
        run.Truncated = true;
        await repository.UpdateAsync(run, CancellationToken.None);

        var persisted = await repository.FindByIdAsync(run.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(truncatedStdout, persisted.Stdout);
        Assert.Equal("some stderr\n", persisted.Stderr);
        Assert.True(persisted.Truncated);
    }

    [Theory]
    [InlineData(RunKind.DotnetCommand)]
    [InlineData(RunKind.GitCommand)]
    [InlineData(RunKind.FileTransfer)]
    public async Task A_row_left_Running_by_a_simulated_crash_is_reconciled_to_Interrupted_on_next_startup(RunKind kind)
    {
        var repository = CreateRepository();
        var run = new Run
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            RepositoryPath = @"C:\repositories\example",
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await repository.AddAsync(run, CancellationToken.None);

        var reconciledCount = await repository.ReconcileInterruptedAsync(CancellationToken.None);

        Assert.Equal(1, reconciledCount);
        var reconciled = await repository.FindByIdAsync(run.Id, CancellationToken.None);
        Assert.NotNull(reconciled);
        Assert.Equal(RunOutcome.Interrupted, reconciled.Outcome);
        Assert.NotNull(reconciled.CompletedAt);
    }

    [Fact]
    public async Task ReconcileInterruptedAsync_leaves_already_terminal_rows_untouched()
    {
        var repository = CreateRepository();
        var completed = new Run
        {
            Id = Guid.NewGuid(),
            Kind = RunKind.DotnetCommand,
            RepositoryPath = @"C:\repositories\example",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Completed,
        };
        await repository.AddAsync(completed, CancellationToken.None);

        var reconciledCount = await repository.ReconcileInterruptedAsync(CancellationToken.None);

        Assert.Equal(0, reconciledCount);
        var stillCompleted = await repository.FindByIdAsync(completed.Id, CancellationToken.None);
        Assert.Equal(RunOutcome.Completed, stillCompleted!.Outcome);
    }

    [Fact]
    public async Task ListAsync_filters_by_a_single_kind()
    {
        var repository = CreateRepository();
        await SeedAsync(repository, Sample(RunKind.DotnetCommand), Sample(RunKind.GitCommand), Sample(RunKind.FileTransfer));

        var results = await repository.ListAsync(new RunQuery { Kinds = [RunKind.GitCommand] }, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(RunKind.GitCommand, results[0].Kind);
    }

    [Fact]
    public async Task ListAsync_filters_by_multiple_kinds()
    {
        var repository = CreateRepository();
        await SeedAsync(repository, Sample(RunKind.DotnetCommand), Sample(RunKind.GitCommand), Sample(RunKind.FileTransfer));

        var results = await repository.ListAsync(new RunQuery { Kinds = [RunKind.GitCommand, RunKind.FileTransfer] }, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.NotEqual(RunKind.DotnetCommand, r.Kind));
    }

    [Fact]
    public async Task ListAsync_filters_by_outcome()
    {
        var repository = CreateRepository();
        await SeedAsync(
            repository,
            Sample(RunKind.DotnetCommand, outcome: RunOutcome.Completed),
            Sample(RunKind.DotnetCommand, outcome: RunOutcome.TimedOut));

        var results = await repository.ListAsync(new RunQuery { Outcomes = [RunOutcome.TimedOut] }, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(RunOutcome.TimedOut, results[0].Outcome);
    }

    [Fact]
    public async Task ListAsync_filters_by_repository_alone()
    {
        var repository = CreateRepository();
        await SeedAsync(
            repository,
            Sample(RunKind.DotnetCommand, repositoryPath: @"C:\repositories\a"),
            Sample(RunKind.DotnetCommand, repositoryPath: @"C:\repositories\b"));

        var results = await repository.ListAsync(new RunQuery { RepositoryPath = @"C:\repositories\a" }, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(@"C:\repositories\a", results[0].RepositoryPath);
    }

    [Fact]
    public async Task ListAsync_free_text_search_matches_repository_arguments_file_path_and_source_url()
    {
        var repository = CreateRepository();
        var byRepository = Sample(RunKind.DotnetCommand, repositoryPath: @"C:\repositories\needle-repository");
        var byArguments = Sample(RunKind.DotnetCommand, arguments: ["build", "needle-arg"]);
        var byFilePath = Sample(RunKind.FileTransfer, filePath: "bin/needle-file.dll");
        var bySourceUrl = Sample(RunKind.RepositoryClone, sourceUrl: "https://example.com/needle-repository.git");
        var nonMatching = Sample(RunKind.DotnetCommand, repositoryPath: @"C:\repositories\unrelated");
        await SeedAsync(repository, byRepository, byArguments, byFilePath, bySourceUrl, nonMatching);

        var results = await repository.ListAsync(new RunQuery { SearchText = "needle" }, CancellationToken.None);

        Assert.Equal(4, results.Count);
        Assert.DoesNotContain(results, r => r.Id == nonMatching.Id);
    }

    [Fact]
    public async Task ListAsync_combines_two_filters_with_AND()
    {
        var repository = CreateRepository();
        await SeedAsync(
            repository,
            Sample(RunKind.DotnetCommand, outcome: RunOutcome.Completed),
            Sample(RunKind.GitCommand, outcome: RunOutcome.Completed),
            Sample(RunKind.DotnetCommand, outcome: RunOutcome.TimedOut));

        var results = await repository.ListAsync(
            new RunQuery { Kinds = [RunKind.DotnetCommand], Outcomes = [RunOutcome.Completed] },
            CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(RunKind.DotnetCommand, results[0].Kind);
        Assert.Equal(RunOutcome.Completed, results[0].Outcome);
    }

    [Fact]
    public async Task ListAsync_combines_search_with_a_filter()
    {
        var repository = CreateRepository();
        var matching = Sample(RunKind.DotnetCommand, repositoryPath: @"C:\repositories\needle-repository");
        var wrongKind = Sample(RunKind.GitCommand, repositoryPath: @"C:\repositories\needle-repository");
        var wrongText = Sample(RunKind.DotnetCommand, repositoryPath: @"C:\repositories\unrelated");
        await SeedAsync(repository, matching, wrongKind, wrongText);

        var results = await repository.ListAsync(
            new RunQuery { Kinds = [RunKind.DotnetCommand], SearchText = "needle" },
            CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(matching.Id, results[0].Id);
    }

    [Fact]
    public async Task ListAsync_with_no_filters_returns_everything()
    {
        var repository = CreateRepository();
        await SeedAsync(repository, Sample(RunKind.DotnetCommand), Sample(RunKind.GitCommand), Sample(RunKind.FileTransfer));

        var results = await repository.ListAsync(new RunQuery(), CancellationToken.None);

        Assert.Equal(3, results.Count);
    }

    private EfRunRepository CreateRepository() => new(_dbContextFactory.CreateContext());

    private static async Task SeedAsync(EfRunRepository repository, params Run[] runs)
    {
        foreach (var run in runs)
        {
            await repository.AddAsync(run, CancellationToken.None);
        }
    }

    private static Run Sample(
        RunKind kind,
        RunOutcome outcome = RunOutcome.Completed,
        string repositoryPath = @"C:\repositories\example",
        IReadOnlyList<string>? arguments = null,
        string? filePath = null,
        string? sourceUrl = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            RepositoryPath = repositoryPath,
            Arguments = arguments,
            FilePath = filePath,
            SourceUrl = sourceUrl,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = outcome,
        };

    /// <inheritdoc />
    public void Dispose() => _dbContextFactory.Dispose();
}
