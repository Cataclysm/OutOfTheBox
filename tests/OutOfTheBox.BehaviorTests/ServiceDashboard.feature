Feature: Service Dashboard
    Mirrors specs/service-dashboard/spec.md's auth-gate, login, and branding requirements - driven
    over plain HTTP against a real running instance of the service (Host, via
    WebApplicationFactory), since the auth cookie exchange, redirect-when-unauthenticated behavior,
    and initial server-rendered HTML are all observable without a real browser/SignalR client.
    Interactive, live-updating dashboard behavior (a run appearing in Status without reload,
    History's filter/search controls) needs genuine Blazor-interactive test tooling this project
    doesn't have yet (see tasks.md's Section 12 deviation notes) and isn't covered here.

    Scenario: Unauthenticated dashboard access
        When an unauthenticated browser requests the dashboard
        Then the dashboard redirects to the login page

    Scenario: Logging in with the correct token
        When the operator logs in with the correct token
        Then a session cookie is issued and the dashboard becomes reachable

    Scenario: Logging in with an incorrect token
        When the operator logs in with an incorrect token
        Then login is rejected back to the login page

    Scenario: Logging out revokes dashboard access
        Given the operator has logged in with the correct token
        When the operator logs out
        Then the dashboard once again redirects to the login page

    Scenario: Service name is visible from any view
        Given the operator has logged in with the correct token
        When the operator opens the Status view
        Then the rendered page shows the service name

    Scenario: Running version is visible on the Status view
        Given the operator has logged in with the correct token
        When the operator opens the Status view
        Then the rendered page shows the running version

    Scenario: The version endpoint is reachable without a credential
        When an unauthenticated request is made for the running version
        Then the response reports the service name and version
