// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OutOfTheBox.BehaviorTests.Support;
using OutOfTheBox.Domain.Repositories;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>RepositoryRestEndpoints.feature</c>.</summary>
[Binding]
public sealed class RepositoryRestEndpointsSteps : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private GitFixture? _gitFixture;
    private CommandExecutionServiceFactory? _factory;
    private HttpResponseMessage? _response;

    private async Task EnsureFactoryAsync()
    {
        if (_factory is not null)
        {
            return;
        }

        _gitFixture = await GitFixture.CreateAsync();
        _factory = new CommandExecutionServiceFactory(rootDirectoryOverride: _gitFixture.RootDirectory);
    }

    private string SourceRepositoryPath => Path.Combine(_gitFixture!.RootDirectory, "GitFixture");

    [Given(@"a repository named ""(.*)"" is present on disk")]
    public async Task GivenARepositoryNamedIsPresentOnDisk(string name)
    {
        await EnsureFactoryAsync();
        Directory.CreateDirectory(Path.Combine(_gitFixture!.RootDirectory, name));
        await File.WriteAllTextAsync(Path.Combine(_gitFixture.RootDirectory, name, "marker.txt"), "present");
    }

    [When(@"an unauthenticated request is made to list repositories")]
    public async Task WhenAnUnauthenticatedRequestIsMadeToListRepositories()
    {
        await EnsureFactoryAsync();
        using var client = _factory!.CreateClient();
        _response = await client.GetAsync("/repositories");
    }

    [When(@"an authenticated caller requests the repository list")]
    public async Task WhenAnAuthenticatedCallerRequestsTheRepositoryList()
    {
        await EnsureFactoryAsync();
        using var client = _factory!.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/repositories");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _response = await client.SendAsync(request, CancellationToken.None);
    }

    [Then(@"the response includes ""(.*)"" with its size, git status, and path")]
    public async Task ThenTheResponseIncludesWithItsSizeAndGitStatus(string name)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        var summaries = await ReadSummariesAsync(_response);

        var match = Assert.Single(summaries, s => s.Name == name);
        Assert.True(match.StatsComputed);
        Assert.True(match.TotalSizeBytes > 0);

        // Path is informational only (per direct instruction: every endpoint that targets a
        // repository does so by name, never by path) but must still be present - it's the one
        // place a caller can get the real on-disk location without deriving it.
        Assert.EndsWith(name, match.Path, StringComparison.OrdinalIgnoreCase);
    }

    [When(@"an unauthenticated request is made to clone a repository")]
    public async Task WhenAnUnauthenticatedRequestIsMadeToCloneARepository()
    {
        await EnsureFactoryAsync();
        using var client = _factory!.CreateClient();
        _response = await client.PostAsJsonAsync("/repositories/clone", new { url = SourceRepositoryPath, name = "unauthorized-attempt" });
    }

    [When(@"an authenticated caller requests a clone of the fixture repository under ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerRequestsACloneOfTheFixtureRepositoryUnder(string name)
    {
        await EnsureFactoryAsync();
        await PostCloneAsync(SourceRepositoryPath, name);
    }

    [When(@"an authenticated caller requests a clone into ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerRequestsACloneInto(string name)
    {
        await EnsureFactoryAsync();
        await PostCloneAsync(SourceRepositoryPath, name);
    }

    [When(@"an authenticated caller requests a clone with no target name")]
    public async Task WhenAnAuthenticatedCallerRequestsACloneWithNoTargetName()
    {
        await EnsureFactoryAsync();
        await PostCloneAsync(SourceRepositoryPath, name: null);
    }

    private async Task PostCloneAsync(string url, string? name)
    {
        using var client = _factory!.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/repositories/clone")
        {
            Content = JsonContent.Create(new { url, name }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _response = await client.SendAsync(request, CancellationToken.None);
    }

    [Then(@"the response is unauthorized")]
    public void ThenTheResponseIsUnauthorized() => Assert.Equal(HttpStatusCode.Unauthorized, _response!.StatusCode);

    [Then(@"the clone request is accepted with a run id")]
    public void ThenTheCloneRequestIsAcceptedWithARunId()
    {
        Assert.Equal(HttpStatusCode.Accepted, _response!.StatusCode);
        Assert.True(_response.Headers.Contains("X-Run-Id"));
    }

    [Then(@"""(.*)"" eventually appears in the repository list")]
    public async Task ThenEventuallyAppearsInTheRepositoryList(string name)
    {
        using var client = _factory!.CreateClient();

        for (var i = 0; i < 100; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/repositories");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);
            using var response = await client.SendAsync(request, CancellationToken.None);
            var summaries = await ReadSummariesAsync(response);

            if (summaries.Any(s => s.Name == name))
            {
                return;
            }

            await Task.Delay(50, CancellationToken.None);
        }

        throw new TimeoutException($"'{name}' did not appear in the repository list in time.");
    }

    [Then(@"the response is a conflict naming the reason ""(.*)""")]
    public async Task ThenTheResponseIsAConflictNamingTheReason(string reason)
    {
        Assert.Equal(HttpStatusCode.Conflict, _response!.StatusCode);
        var body = await _response.Content.ReadAsStringAsync();
        Assert.Contains(reason, body, StringComparison.Ordinal);
    }

    [Then(@"the response is a validation error")]
    public void ThenTheResponseIsAValidationError() => Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);

    private static async Task<List<RepositorySummary>> ReadSummariesAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<List<RepositorySummary>>(JsonOptions) ?? [];

    /// <inheritdoc />
    public void Dispose()
    {
        _response?.Dispose();
        _factory?.Dispose();
        _gitFixture?.Dispose();
    }
}
