Feature: MCP Command Execution
    Mirrors specs/mcp-command-execution/spec.md, driven against a real running instance of the
    service (Host, via WebApplicationFactory) pointed at the checked-in fixture repositories, so real
    dotnet.exe/git.exe child processes are genuinely spawned. MCP is this service's only
    command-execution interface, so this feature also carries the locking/concurrency coverage
    previously split across separate ConcurrencyAndLocking/Cancellation features - dotnet_run/git_run
    share one RunRegistry lock per repository regardless of which tool (or which kind) is asking,
    since both funnel through the same internal start/run-to-completion code path, parameterized only
    by which executable runs.

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

    Scenario: Cancelling an in-flight run kills it and frees the repository
        Given an in-flight dotnet_run against "HangingFixture" with a 30 second timeout
        When the caller calls cancel_run for that run
        Then read_run_output eventually reports status "cancelled"
        And a subsequent dotnet_run against "HangingFixture" is accepted

    Scenario: Cancelling a run that has already finished
        Given a dotnet_run against "PassingFixture" has already completed
        When the caller calls cancel_run for that run
        Then cancel_run returns the run's existing status without error

    Scenario: Cancelling an unknown run id is rejected
        When the caller calls cancel_run for an unknown run id
        Then the MCP call is rejected

    Scenario: Commands against different repositories run in parallel
        When authenticated dotnet_run calls are started concurrently against "PassingFixture" and "FailingFixture"
        Then both concurrent runs complete independently

    Scenario: A second dotnet_run for a busy repository is rejected
        Given an in-flight dotnet_run against "HangingFixture" with a 30 second timeout
        When an authenticated caller starts a dotnet_run "test" against "HangingFixture"
        Then the MCP call is rejected

    Scenario: The repository becomes available again once the in-flight run completes on its own
        Given an in-flight dotnet_run against "HangingFixture" with a 3 second timeout
        When that run reaches a terminal state
        And an authenticated caller starts a dotnet_run against "HangingFixture" with a 3 second timeout
        Then an MCP run id is returned

    Scenario: A git_run is rejected while a dotnet_run is in flight for the same repository
        Given an in-flight dotnet_run against "HangingFixture" with a 30 second timeout
        When an authenticated caller starts a git_run "status" against "HangingFixture"
        Then the MCP call is rejected

    Scenario: An unknown dotnet subcommand is rejected
        When an authenticated caller starts a dotnet_run "obliterate" against "PassingFixture"
        Then the MCP call is rejected

    Scenario: An unknown git subcommand is rejected
        When an authenticated caller starts a git_run "nuke" against the git fixture
        Then the MCP call is rejected

    Scenario: A dotnet_run argument that would escape the repository is rejected
        When an authenticated caller starts a dotnet_run "test" with an escaping --results-directory against "PassingFixture"
        Then the MCP call is rejected

    Scenario: A subcommand disabled via MCP Settings is rejected even though it's in the known catalog
        Given the "status" subcommand is disabled for git in MCP Settings
        When an authenticated caller starts a git_run "status" against the git fixture
        Then the MCP call is rejected
