// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>GitCommandExecution.feature</c>.</summary>
[Binding]
public sealed class GitCommandExecutionSteps : IDisposable
{
    private GitFixture? _gitFixture;
    private CommandExecutionServiceFactory? _factory;
    private HttpResponseMessage? _response;
    private IReadOnlyList<SseEvent> _events = [];

    [When(@"an authenticated caller starts a git run with arguments ""(.*)"" against the git fixture")]
    public async Task WhenAnAuthenticatedCallerStartsAGitRunWithArgumentsAgainstTheGitFixture(string commaSeparatedArguments)
    {
        _gitFixture = await GitFixture.CreateAsync();
        _factory = new CommandExecutionServiceFactory(rootDirectoryOverride: _gitFixture.RootDirectory);
        using var client = _factory.CreateClient();

        var arguments = commaSeparatedArguments.Split(',');

        var result = await SseTestClient.PostAndReadAllEventsAsync(
            client,
            "/run/git",
            new { arguments, workingDirectory = "GitFixture" },
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);

        _response = result.Response;
        _events = result.Events;
    }

    [Then(@"a git run id is returned")]
    public void ThenAGitRunIdIsReturned() => Assert.True(_response!.Headers.Contains("X-Run-Id"));

    [Then(@"the git run completes with exit code (\d+)")]
    public void ThenTheGitRunCompletesWithExitCode(int expectedExitCode)
    {
        var doneEvent = Assert.Single(_events, e => e.Name == "done");
        using var payload = JsonDocument.Parse(doneEvent.Data);
        Assert.Equal(expectedExitCode, payload.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Then(@"the git run completes with a non-zero exit code")]
    public void ThenTheGitRunCompletesWithANonZeroExitCode()
    {
        var doneEvent = Assert.Single(_events, e => e.Name == "done");
        using var payload = JsonDocument.Parse(doneEvent.Data);
        Assert.NotEqual(0, payload.RootElement.GetProperty("exitCode").GetInt32());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _response?.Dispose();
        _factory?.Dispose();
        _gitFixture?.Dispose();
    }
}
