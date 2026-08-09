// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.BehaviorTests.Support;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>RunHistoryPersistence.feature</c>. Drives completion via MCP tool calls (dotnet_run/git_run/transfer_file) - persistence itself is interface-agnostic, but something has to actually produce a run to persist.</summary>
[Binding]
public sealed class RunHistoryPersistenceSteps : IDisposable
{
    private GitFixture? _gitFixture;
    private CommandExecutionServiceFactory? _factory;
    private string? _sqliteFilePath;
    private Guid _runId;
    private Run? _reloadedRun;

    [When(@"a dotnet run completes against ""(.*)""")]
    public async Task WhenADotnetRunCompletesAgainst(string fixtureName)
    {
        _factory = new CommandExecutionServiceFactory();
        _sqliteFilePath = _factory.SqliteFilePath;
        using var client = _factory.CreateClient();

        var start = await McpTestClient.CallToolAsync(
            client, "dotnet_run", new { arguments = new[] { "--version" }, workingDirectory = fixtureName }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        Assert.False(start.IsToolError, start.ContentText);
        _runId = JsonDocument.Parse(start.ContentText!).RootElement.GetProperty("runId").GetGuid();

        await PollUntilTerminalAsync(client, _runId);
    }

    [When(@"a git run completes against the git fixture")]
    public async Task WhenAGitRunCompletesAgainstTheGitFixture()
    {
        _gitFixture = await GitFixture.CreateAsync();
        _factory = new CommandExecutionServiceFactory(rootDirectoryOverride: _gitFixture.RootDirectory);
        _sqliteFilePath = _factory.SqliteFilePath;
        using var client = _factory.CreateClient();

        var start = await McpTestClient.CallToolAsync(
            client, "git_run", new { arguments = new[] { "status" }, workingDirectory = "GitFixture" }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        Assert.False(start.IsToolError, start.ContentText);
        _runId = JsonDocument.Parse(start.ContentText!).RootElement.GetProperty("runId").GetGuid();

        await PollUntilTerminalAsync(client, _runId);
    }

    [When(@"a transfer of ""(.*)"" from ""(.*)"" completes")]
    public async Task WhenATransferOfFromCompletes(string path, string repository)
    {
        _factory = new CommandExecutionServiceFactory();
        _sqliteFilePath = _factory.SqliteFilePath;
        using var client = _factory.CreateClient();

        var result = await McpTestClient.CallToolAsync(
            client, "transfer_file", new { repository, path }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
        Assert.False(result.IsToolError, result.ContentText);
        _runId = JsonDocument.Parse(result.ContentText!).RootElement.GetProperty("runId").GetGuid();
    }

    private static async Task PollUntilTerminalAsync(HttpClient client, Guid runId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var result = await McpTestClient.CallToolAsync(
                client, "read_run_output", new { runId, offset = 0 }, CommandExecutionServiceFactory.TestBearerToken, CancellationToken.None);
            Assert.False(result.IsToolError, result.ContentText);

            if (JsonDocument.Parse(result.ContentText!).RootElement.GetProperty("status").GetString() != "running")
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Run {runId} did not reach a terminal state in time.");
            }

            await Task.Delay(100);
        }
    }

    [When(@"the service is restarted")]
    public async Task WhenTheServiceIsRestarted()
    {
        // Disposing the first factory tears down its host (and Kestrel test server) entirely;
        // creating a second factory pointed at the same SQLite file - rather than reusing the
        // first - is what actually exercises "does the record survive," since a fresh factory
        // re-runs Program.cs's startup migration/reconciliation against the existing file exactly
        // like a real service restart would.
        _factory!.Dispose();

        var restarted = new CommandExecutionServiceFactory(sqliteFilePathOverride: _sqliteFilePath);
        _factory = restarted;

        await using var scope = restarted.Services.CreateAsyncScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        _reloadedRun = await runRepository.FindByIdAsync(_runId, CancellationToken.None);
    }

    [Then(@"the persisted history record for that run shows outcome ""(.*)""")]
    public void ThenThePersistedHistoryRecordForThatRunShowsOutcome(string expectedOutcome)
    {
        Assert.NotNull(_reloadedRun);
        Assert.Equal(expectedOutcome, _reloadedRun.Outcome.ToString());
    }

    [Then(@"it has a recorded file size")]
    public void ThenItHasARecordedFileSize()
    {
        Assert.NotNull(_reloadedRun);
        Assert.NotNull(_reloadedRun.FileSizeBytes);
        Assert.True(_reloadedRun.FileSizeBytes > 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _factory?.Dispose();
        _gitFixture?.Dispose();
    }
}
