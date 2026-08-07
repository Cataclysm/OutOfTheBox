Feature: Dotnet Command Execution
    Mirrors specs/dotnet-command-execution/spec.md, driven against a real running instance of the
    service (Host, via WebApplicationFactory) pointed at the checked-in fixture repos, so real
    dotnet.exe child processes are genuinely spawned - this is the behavior under test.

    Scenario: Successful test run round trip
        When an authenticated caller starts "test" against "PassingFixture"
        Then a run id is returned
        And the run completes with exit code 0
        And output events were received before completion

    Scenario: Failing test run returns a non-zero exit code
        When an authenticated caller starts "test" against "FailingFixture"
        Then the run completes with a non-zero exit code

    Scenario: A non-streaming client still receives every event
        When a non-streaming authenticated caller starts "test" against "PassingFixture"
        Then the run completes with exit code 0

    Scenario: No caller-supplied timeout falls back to the configured default
        Given the configured default execution timeout is 5 seconds
        When an authenticated caller starts a run with no timeout against "HangingFixture"
        Then the run is killed with reason "timeout"

    Scenario: A short caller-supplied timeout is honored over the default
        When an authenticated caller starts a run with a 5 second timeout against "HangingFixture"
        Then the run is killed with reason "timeout"
