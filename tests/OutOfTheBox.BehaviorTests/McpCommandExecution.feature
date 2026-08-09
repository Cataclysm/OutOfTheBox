Feature: MCP Command Execution
    Mirrors specs/mcp-command-execution/spec.md, driven against a real running instance of the
    service (Host, via WebApplicationFactory) pointed at the checked-in fixture repositories, so real
    dotnet.exe/git.exe child processes are genuinely spawned - the same "real process" discipline
    DotnetCommandExecution.feature/GitCommandExecution.feature already use for the REST API. The
    cross-interface locking scenarios are this feature's highest-risk coverage - per design.md, MCP
    and REST share the exact same RunRegistry lock, and that sharing is unverified until proven live.

    Scenario: Starting a dotnet command and polling it to completion
        When an authenticated caller starts a dotnet_run "test" against "PassingFixture"
        Then an MCP run id is returned
        And read_run_output eventually reports status "completed" with exit code 0

    Scenario: A failing dotnet command reports a non-zero exit code
        When an authenticated caller starts a dotnet_run "test" against "FailingFixture"
        Then read_run_output eventually reports a non-zero exit code

    Scenario: Starting a git command
        When an authenticated caller starts a git_run "status" against the git fixture
        Then read_run_output eventually reports status "completed" with exit code 0

    Scenario: Polling from a non-zero offset only returns new output
        When an authenticated caller starts a dotnet_run "test" against "PassingFixture"
        And read_run_output is called once it reaches a terminal state
        And read_run_output is called again with the offset from the previous call
        Then the second read_run_output call returns no additional output

    Scenario: read_run_output on an unknown run id is rejected
        When an authenticated caller calls read_run_output with an unknown run id
        Then the MCP call is rejected

    Scenario: A caller-supplied timeout is honored
        When an authenticated caller starts a dotnet_run against "HangingFixture" with a 3 second timeout
        Then read_run_output eventually reports status "timed out"

    Scenario: Cancelling an in-flight run
        Given an in-flight dotnet_run against "HangingFixture" with a 30 second timeout
        When the caller calls cancel_run for that run
        Then read_run_output eventually reports status "cancelled"

    Scenario: Cancelling a run that has already finished
        Given a dotnet_run against "PassingFixture" has already completed
        When the caller calls cancel_run for that run
        Then cancel_run returns the run's existing status without error

    Scenario: Cancelling an unknown run id is rejected
        When the caller calls cancel_run for an unknown run id
        Then the MCP call is rejected

    Scenario: A dotnet_run is rejected while a REST run is in flight for the same repository
        Given a REST run is in flight against "HangingFixture"
        When an authenticated caller starts a dotnet_run "test" against "HangingFixture"
        Then the MCP call is rejected

    Scenario: A REST run is rejected while a dotnet_run is in flight for the same repository
        Given an in-flight dotnet_run against "HangingFixture" with a 3 second timeout
        When a REST run is started against "HangingFixture"
        Then the REST run is rejected as a repository conflict
