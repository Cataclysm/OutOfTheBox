// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.BehaviorTests.Support;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>RunHistoryPersistence.feature</c>.</summary>
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

        var result = await SseTestClient.PostAndReadAllEventsAsync(
            client,
            "/run",
            new { arguments = new[] { "--version" }, workingDirectory = fixtureName },
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);

        Assert.Contains(result.Events, e => e.Name == "done");
        _runId = Guid.Parse(result.Response.Headers.GetValues("X-Run-Id").Single());
        result.Response.Dispose();
    }

    [When(@"a git run completes against the git fixture")]
    public async Task WhenAGitRunCompletesAgainstTheGitFixture()
    {
        _gitFixture = await GitFixture.CreateAsync();
        _factory = new CommandExecutionServiceFactory(rootDirectoryOverride: _gitFixture.RootDirectory);
        _sqliteFilePath = _factory.SqliteFilePath;
        using var client = _factory.CreateClient();

        var result = await SseTestClient.PostAndReadAllEventsAsync(
            client,
            "/run/git",
            new { arguments = new[] { "status" }, workingDirectory = "GitFixture" },
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);

        Assert.Contains(result.Events, e => e.Name == "done");
        _runId = Guid.Parse(result.Response.Headers.GetValues("X-Run-Id").Single());
        result.Response.Dispose();
    }

    [When(@"a transfer of ""(.*)"" from ""(.*)"" completes")]
    public async Task WhenATransferOfFromCompletes(string path, string repo)
    {
        _factory = new CommandExecutionServiceFactory();
        _sqliteFilePath = _factory.SqliteFilePath;
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/artifacts")
        {
            Content = JsonContent.Create(new { repo, path }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        using var response = await client.SendAsync(request, CancellationToken.None);
        await response.Content.ReadAsByteArrayAsync();
        _runId = Guid.Parse(response.Headers.GetValues("X-Run-Id").Single());
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

    [Then(@"it has a recorded artifact size")]
    public void ThenItHasARecordedArtifactSize()
    {
        Assert.NotNull(_reloadedRun);
        Assert.NotNull(_reloadedRun.ArtifactSizeBytes);
        Assert.True(_reloadedRun.ArtifactSizeBytes > 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _factory?.Dispose();
        _gitFixture?.Dispose();
    }
}
