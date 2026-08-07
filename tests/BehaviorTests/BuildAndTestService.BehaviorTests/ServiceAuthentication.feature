Feature: Service Authentication
    Mirrors specs/service-authentication/spec.md. Exercises the real BearerAuthenticationFilter
    directly through ASP.NET Core's endpoint-filter invocation contract (EndpointFilterInvocationContext),
    not yet through a real mapped HTTP endpoint - the command-execution endpoint that "does not invoke
    dotnet.exe" is wired up in Section 5, where that specific claim gets its own coverage.

    Scenario: Missing credential
        Given the configured bearer token is "expected-token"
        And the request has no Authorization header
        When the request passes through the bearer authentication filter
        Then the request is rejected with an authentication error
        And the protected handler is not invoked

    Scenario: Valid credential
        Given the configured bearer token is "expected-token"
        And the request includes the bearer credential "expected-token"
        When the request passes through the bearer authentication filter
        Then the request proceeds to the protected handler

    Scenario: Wrong credential
        Given the configured bearer token is "expected-token"
        And the request includes the bearer credential "wrong-token"
        When the request passes through the bearer authentication filter
        Then the request is rejected with an authentication error
        And the protected handler is not invoked

    Scenario: Credential rotation
        Given the configured bearer token is "old-token"
        And the request includes the bearer credential "old-token"
        When the request passes through the bearer authentication filter
        Then the request proceeds to the protected handler
        When the configured bearer token is changed to "new-token"
        And the request includes the bearer credential "old-token"
        And the request passes through the bearer authentication filter
        Then the request is rejected with an authentication error
