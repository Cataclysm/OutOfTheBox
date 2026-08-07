Feature: Git Command Execution
    Mirrors specs/git-command-execution/spec.md, driven against a real running instance of the
    service (Host, via WebApplicationFactory) pointed at a freshly-generated GitFixture, so real
    git.exe child processes are genuinely spawned - this is the behavior under test.

    Scenario: Successful command round trip
        When an authenticated caller starts a git run with arguments "status" against the git fixture
        Then a git run id is returned
        And the git run completes with exit code 0

    Scenario: A failing git command returns a non-zero exit code
        When an authenticated caller starts a git run with arguments "checkout,does-not-exist" against the git fixture
        Then the git run completes with a non-zero exit code

    Scenario: A destructive command is accepted without restriction
        When an authenticated caller starts a git run with arguments "reset,--hard,HEAD" against the git fixture
        Then the git run completes with exit code 0
