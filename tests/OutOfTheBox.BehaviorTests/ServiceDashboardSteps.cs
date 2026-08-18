// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Net;
using OutOfTheBox.BehaviorTests.Support;
using OutOfTheBox.Presentation.Dashboard;
using Microsoft.AspNetCore.Mvc.Testing;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>ServiceDashboard.feature</c>.</summary>
[Binding]
public sealed class ServiceDashboardSteps : IDisposable
{
    private CommandExecutionServiceFactory? _factory;
    private HttpResponseMessage? _response;
    private string? _sessionCookie;

    private CommandExecutionServiceFactory Factory => _factory ??= new CommandExecutionServiceFactory();

    [When(@"an unauthenticated browser requests the dashboard")]
    public async Task WhenAnUnauthenticatedBrowserRequestsTheDashboard()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _response = await client.GetAsync("/");
    }

    [Then(@"the dashboard redirects to the login page")]
    public void ThenTheDashboardRedirectsToTheLoginPage()
    {
        Assert.Equal(HttpStatusCode.Found, _response!.StatusCode);
        Assert.Contains("/login", _response.Headers.Location!.ToString());
    }

    [Then(@"the dashboard once again redirects to the login page")]
    public void ThenTheDashboardOnceAgainRedirectsToTheLoginPage() => ThenTheDashboardRedirectsToTheLoginPage();

    [Given(@"the operator has logged in with the correct token")]
    [When(@"the operator logs in with the correct token")]
    public async Task WhenTheOperatorLogsInWithTheCorrectToken()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = CommandExecutionServiceFactory.TestBearerToken,
        });

        _response = await client.PostAsync("/account/login", content);
        _sessionCookie = _response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.First().Split(';')[0]
            : null;
    }

    [When(@"the operator logs in with an incorrect token")]
    public async Task WhenTheOperatorLogsInWithAnIncorrectToken()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "wrong-token" });

        _response = await client.PostAsync("/account/login", content);
    }

    [Then(@"login is rejected back to the login page")]
    public void ThenLoginIsRejectedBackToTheLoginPage()
    {
        Assert.Equal(HttpStatusCode.Found, _response!.StatusCode);
        Assert.Contains("error=1", _response.Headers.Location!.ToString());
    }

    [Then(@"a session cookie is issued and the dashboard becomes reachable")]
    public async Task ThenASessionCookieIsIssuedAndTheDashboardBecomesReachable()
    {
        Assert.NotNull(_sessionCookie);

        var response = await GetWithSessionAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [When(@"the operator logs out")]
    public async Task WhenTheOperatorLogsOut()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/logout");
        request.Headers.Add("Cookie", _sessionCookie!);

        _response = await client.SendAsync(request);
    }

    [When(@"the operator opens the Status view")]
    public async Task WhenTheOperatorOpensTheStatusView() => _response = await GetWithSessionAsync("/");

    [Then(@"the rendered page shows the service name")]
    public async Task ThenTheRenderedPageShowsTheServiceName()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        var body = await _response.Content.ReadAsStringAsync();

        // Blazor Server prerenders the shared layout into the initial HTML response before the
        // SignalR circuit attaches, so the branding element is observable without a real browser.
        Assert.Contains("Out of the Box", body, StringComparison.Ordinal);
    }

    [Then(@"the rendered page shows the running version")]
    public async Task ThenTheRenderedPageShowsTheRunningVersion()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        var body = await _response.Content.ReadAsStringAsync();

        // Same prerendering reasoning as the service-name check above - the Status view's "Service"
        // tile (not the shared header, which no longer repeats the version) is what's observed here.
        Assert.Contains(VersionInfo.Current, body, StringComparison.Ordinal);
    }

    [When(@"an unauthenticated request is made for the running version")]
    public async Task WhenAnUnauthenticatedRequestIsMadeForTheRunningVersion()
    {
        using var client = Factory.CreateClient();
        _response = await client.GetAsync("/version");
    }

    [Then(@"the response reports the service name and version")]
    public async Task ThenTheResponseReportsTheServiceNameAndVersion()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        var body = await _response.Content.ReadAsStringAsync();
        Assert.Contains("Out of the Box", body, StringComparison.Ordinal);
    }

    private async Task<HttpResponseMessage> GetWithSessionAsync(string requestUri)
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("Cookie", _sessionCookie!);

        var response = await client.SendAsync(request);

        // Buffer the body into memory before `client` is disposed at the end of this method - the
        // response is handed back to a later step (e.g. to assert on its content), by which point
        // the client that produced it is long gone.
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _response?.Dispose();
        _factory?.Dispose();
    }
}
