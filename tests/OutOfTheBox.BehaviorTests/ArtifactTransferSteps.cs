// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OutOfTheBox.BehaviorTests.Support;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>ArtifactTransfer.feature</c>.</summary>
[Binding]
public sealed class ArtifactTransferSteps : IDisposable
{
    private CommandExecutionServiceFactory? _factory;
    private HttpClient? _client;
    private HttpResponseMessage? _response;
    private HttpResponseMessage? _cancelResponse;
    private HttpResponseMessage? _inFlightCommandResponse;
    private byte[]? _receivedBytes;
    private string? _sourceFilePath;
    private string? _largeFilePath;
    private Guid _transferRunId;

    private CommandExecutionServiceFactory Factory => _factory ??= new CommandExecutionServiceFactory(defaultExecutionTimeoutSeconds: 30);

    private HttpClient Client => _client ??= Factory.CreateClient();

    [Given(@"a dotnet test run has produced ""(.*)"" in ""(.*)""")]
    public async Task GivenADotnetTestRunHasProduced(string relativeFilePath, string fixtureName)
    {
        using var client = Factory.CreateClient();
        var result = await SseTestClient.PostAndReadAllEventsAsync(
            client,
            "/run",
            new { arguments = new[] { "test", "--logger", "trx;LogFileName=results.trx", "--results-directory", "TestResults" }, workingDirectory = fixtureName },
            CommandExecutionServiceFactory.TestBearerToken,
            streaming: true,
            CancellationToken.None);

        Assert.Contains(result.Events, e => e.Name == "done");
        result.Response.Dispose();

        var producedPath = Path.Combine(CommandExecutionServiceFactory.FindFixturesRoot(), fixtureName, relativeFilePath);
        Assert.True(File.Exists(producedPath), $"Expected '{relativeFilePath}' to exist in '{fixtureName}' after the test run.");
    }

    [Given(@"a command run is in flight against ""(.*)""")]
    public async Task GivenACommandRunIsInFlightAgainst(string fixtureName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/run")
        {
            Content = JsonContent.Create(new { arguments = new[] { "test" }, workingDirectory = fixtureName }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _inFlightCommandResponse = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
    }

    [Given(@"a large file ""(.*)"" exists in ""(.*)""")]
    public void GivenALargeFileExistsIn(string fileName, string fixtureName)
    {
        _largeFilePath = Path.Combine(CommandExecutionServiceFactory.FindFixturesRoot(), fixtureName, fileName);
        File.WriteAllBytes(_largeFilePath, new byte[100 * 1024 * 1024]);
    }

    [When(@"an authenticated caller transfers ""(.*)"" from ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerTransfers(string path, string repo)
    {
        _sourceFilePath = Path.Combine(CommandExecutionServiceFactory.FindFixturesRoot(), repo, path);

        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/artifacts")
        {
            Content = JsonContent.Create(new { repo, path }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _response = await client.SendAsync(request, CancellationToken.None);
        _receivedBytes = await _response.Content.ReadAsByteArrayAsync();
    }

    [When(@"an authenticated caller starts transferring ""(.*)"" from ""(.*)""")]
    public async Task WhenAnAuthenticatedCallerStartsTransferring(string path, string repo)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/artifacts")
        {
            Content = JsonContent.Create(new { repo, path }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);

        _response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
        _transferRunId = Guid.Parse(_response.Headers.GetValues("X-Run-Id").Single());
    }

    [When(@"that transfer is cancelled")]
    public async Task WhenThatTransferIsCancelled()
    {
        // A separate client from the one the still-open transfer response is on - concurrent
        // requests on the same HttpClient while one is mid-stream were found unreliable during
        // Section 9's git work.
        using var cancelClient = Factory.CreateClient();
        using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/run/{_transferRunId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CommandExecutionServiceFactory.TestBearerToken);
        _cancelResponse = await cancelClient.SendAsync(cancelRequest, CancellationToken.None);

        // Drain whatever bytes made it through before cancellation stopped the copy. The response
        // declared its full Content-Length upfront, so a connection that ends early after
        // cancellation is expected to surface as an exception here, not just a short read - either
        // way, whatever was buffered into `buffer` before that point is what the caller received.
        var buffer = new MemoryStream();
        try
        {
            await using var stream = await _response!.Content.ReadAsStreamAsync();
            await stream.CopyToAsync(buffer);
        }
        catch (IOException)
        {
        }
        catch (HttpRequestException)
        {
        }

        _receivedBytes = buffer.ToArray();
    }

    [Then(@"the transfer's run id is returned")]
    public void ThenTheTransferSRunIdIsReturned()
    {
        Assert.True(_response!.Headers.Contains("X-Run-Id"));
    }

    [Then(@"the transferred bytes match the source file exactly")]
    public void ThenTheTransferredBytesMatchTheSourceFileExactly()
    {
        var expected = File.ReadAllBytes(_sourceFilePath!);
        Assert.Equal(expected, _receivedBytes);
    }

    [Then(@"the transfer is rejected as a confinement violation")]
    public void ThenTheTransferIsRejectedAsAConfinementViolation()
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);
    }

    [Then(@"the transfer is rejected as not found")]
    public void ThenTheTransferIsRejectedAsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, _response!.StatusCode);
    }

    [Then(@"the transfer cancel request is accepted")]
    public void ThenTheTransferCancelRequestIsAccepted()
    {
        Assert.Equal(HttpStatusCode.Accepted, _cancelResponse!.StatusCode);
    }

    [Then(@"fewer bytes were received than the file's full size")]
    public void ThenFewerBytesWereReceivedThanTheFileSFullSize()
    {
        var fullSize = new FileInfo(_largeFilePath!).Length;
        Assert.True(
            (_receivedBytes?.Length ?? 0) < fullSize,
            $"Expected fewer than {fullSize} bytes after cancellation, got {_receivedBytes?.Length ?? 0}.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _response?.Dispose();
        _cancelResponse?.Dispose();
        _inFlightCommandResponse?.Dispose();
        _client?.Dispose();
        _factory?.Dispose();

        // Best-effort cleanup of files written into the checked-in fixtures by these scenarios -
        // TestResults/ is already gitignored and the large file is deleted outright, so a crashed
        // run leaving one behind would show up in `git status`, not silently linger untracked.
        try
        {
            var testResultsDir = Path.Combine(CommandExecutionServiceFactory.FindFixturesRoot(), "PassingFixture", "TestResults");
            if (Directory.Exists(testResultsDir))
            {
                Directory.Delete(testResultsDir, recursive: true);
            }

            if (_largeFilePath is not null && File.Exists(_largeFilePath))
            {
                File.Delete(_largeFilePath);
            }
        }
        catch (IOException)
        {
        }
    }
}
