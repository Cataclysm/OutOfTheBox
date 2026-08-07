// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Presentation.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>ServiceAuthentication.feature</c>.</summary>
[Binding]
public sealed class ServiceAuthenticationSteps
{
    private ServiceOptions _serviceOptions = new();
    private string? _providedCredential;
    private bool _handlerWasInvoked;
    private IResult? _result;

    [Given(@"the configured bearer token is ""(.*)""")]
    public void GivenTheConfiguredBearerTokenIs(string token)
    {
        _serviceOptions = new ServiceOptions { BearerToken = token };
    }

    [Given(@"the request has no Authorization header")]
    public void GivenTheRequestHasNoAuthorizationHeader()
    {
        _providedCredential = null;
    }

    [StepDefinition(@"the request includes the bearer credential ""(.*)""")]
    public void GivenTheRequestIncludesTheBearerCredential(string credential)
    {
        _providedCredential = credential;
    }

    [When(@"the configured bearer token is changed to ""(.*)""")]
    public void WhenTheConfiguredBearerTokenIsChangedTo(string token)
    {
        _serviceOptions = new ServiceOptions { BearerToken = token };
    }

    [When(@"the request passes through the bearer authentication filter")]
    public async Task WhenTheRequestPassesThroughTheBearerAuthenticationFilter()
    {
        _handlerWasInvoked = false;

        var httpContext = new DefaultHttpContext();
        if (_providedCredential is not null)
        {
            httpContext.Request.Headers.Authorization = $"Bearer {_providedCredential}";
        }

        var invocationContext = EndpointFilterInvocationContext.Create(httpContext);
        var filter = new BearerAuthenticationFilter(Options.Create(_serviceOptions));

        var outcome = await filter.InvokeAsync(invocationContext, _ =>
        {
            _handlerWasInvoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        _result = outcome as IResult;
    }

    [Then(@"the request is rejected with an authentication error")]
    public void ThenTheRequestIsRejectedWithAnAuthenticationError()
    {
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(_result);
    }

    [Then(@"the request proceeds to the protected handler")]
    public void ThenTheRequestProceedsToTheProtectedHandler()
    {
        Assert.True(_handlerWasInvoked);
    }

    [Then(@"the protected handler is not invoked")]
    public void ThenTheProtectedHandlerIsNotInvoked()
    {
        Assert.False(_handlerWasInvoked);
    }
}
