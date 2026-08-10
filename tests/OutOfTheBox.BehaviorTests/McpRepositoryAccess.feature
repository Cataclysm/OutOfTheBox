Feature: MCP Repository Access
    Mirrors specs/mcp-repository-access/spec.md - list and clone only, the subset of
    repository-management that's MCP-reachable at all. Driven against a real running instance of the
    service (Host, via WebApplicationFactory), reusing GitFixture so clone_repository genuinely runs
    `git clone`.

    Scenario: Listing repositories
        Given an existing repository named "existing-repository" is on disk for MCP access
        When an authenticated caller calls list_repositories
        Then the result includes "existing-repository" with its size and git status

    Scenario: Cloning a repository
        When an authenticated caller calls clone_repository for the fixture repository under "mcp-cloned-repository"
        Then an MCP clone run id is returned
        And "mcp-cloned-repository" eventually appears via list_repositories

    Scenario: Cloning into an existing name is rejected
        Given an existing repository named "existing-repository" is on disk for MCP access
        When an authenticated caller calls clone_repository with the name "existing-repository"
        Then the clone_repository call is rejected

    Scenario: A clone's run id is accepted by cancel_run
        When an authenticated caller calls clone_repository for the fixture repository under "mcp-cancellable-clone"
        And the caller cancels that clone via cancel_run
        Then cancel_run does not reject the clone's run id as unknown
