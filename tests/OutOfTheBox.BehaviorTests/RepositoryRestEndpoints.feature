Feature: Repository REST Endpoints
    Mirrors specs/repository-management's REST-reachable subset (list and clone only - deletion
    stays dashboard-only, no REST surface, per direct instruction). Driven over real HTTP against a
    real running instance of the service (Host, via WebApplicationFactory), reusing GitFixture so
    `git clone` genuinely runs - the same fixture RepositoryManagement.feature's own
    IRepositoryManager-level scenarios already use for their business-logic coverage. This file
    instead covers the HTTP contract: authentication, status codes, and response shape.

    Scenario: Listing repositories requires a bearer credential
        When an unauthenticated request is made to list repositories
        Then the response is unauthorized

    Scenario: An authenticated caller lists repositories with their metadata
        Given a repository named "existing-repository" is present on disk
        When an authenticated caller requests the repository list
        Then the response includes "existing-repository" with its size, git status, and path

    Scenario: Cloning requires a bearer credential
        When an unauthenticated request is made to clone a repository
        Then the response is unauthorized

    Scenario: An authenticated caller successfully clones a repository
        When an authenticated caller requests a clone of the fixture repository under "rest-cloned-repository"
        Then the clone request is accepted with a run id
        And "rest-cloned-repository" eventually appears in the repository list

    Scenario: Cloning into an existing name is rejected as a conflict
        Given a repository named "existing-repository" is present on disk
        When an authenticated caller requests a clone into "existing-repository"
        Then the response is a conflict naming the reason "already-exists"

    Scenario: Cloning without a target name is rejected as invalid
        When an authenticated caller requests a clone with no target name
        Then the response is a validation error
