Feature: Repository Management
    Mirrors specs/repository-management/spec.md. Repository management has no bearer-token REST
    surface (it's dashboard-only), so these scenarios call IRepositoryManager directly from a
    resolved DI scope - the same way Blazor component code-behind would - rather than through HTTP,
    against a real local git source (reusing GitFixture, the same fixture git-command-execution
    uses) so `git clone` genuinely runs, not a network-dependent remote URL.

    Scenario: Successful clone
        When an operator clones the fixture repository under a new name
        Then the clone succeeds and the new repository appears in the repository list
        And a history record exists for the clone with its source URL and a completed outcome

    Scenario: Clone target name already exists
        Given a repository already exists under a given name
        When an operator attempts to clone into that same name
        Then the clone is rejected as already existing
        And the existing repository's contents are untouched

    Scenario: Duplicate concurrent clone is rejected
        Given a clone into a given name is already in flight
        When a second clone into that same name is requested before the first finishes
        Then the second clone is rejected as a conflict identifying the in-flight run

    Scenario: Commands against a mid-clone target are rejected
        Given a clone into a given name is already in flight
        When a git command targets that same partially-cloned repository
        Then the command is rejected as a repository conflict

    Scenario: Successful deletion
        Given an idle repository exists
        When an operator deletes that repository
        Then the repository no longer exists on disk or in the repository list
        And a history record exists for the deletion with a completed outcome

    Scenario: Deleting a nonexistent repository
        When an operator attempts to delete a name that does not exist
        Then the deletion is rejected as not found

    Scenario: Deletion fails cleanly when a file inside the repository is locked
        Given an idle repository exists
        And a file inside that repository is locked open
        When an operator deletes that repository
        Then the deletion is accepted but the run records a failed outcome with error detail
        And the repository still exists on disk

    Scenario: Deletion succeeds even when a file inside the repository is read-only
        Given an idle repository exists
        And a file inside that repository is read-only
        When an operator deletes that repository
        Then the repository no longer exists on disk or in the repository list
        And a history record exists for the deletion with a completed outcome

    Scenario: Deletion of a busy repository is rejected
        Given a command run is in flight against a repository
        When an operator attempts to delete that repository
        Then the deletion is rejected as a conflict identifying the in-flight run
        And the repository's files are untouched
        And the in-flight run is still running

    Scenario: The REST cancel endpoint does not affect repository-management runs
        Given a clone into a given name is already in flight
        When a bearer-token caller sends a cancellation request naming the clone's run id
        Then the system responds as if the run id were unknown

    Scenario: Cancelling a clone from the dashboard
        Given a clone into a given name is already in flight
        When an operator cancels that clone from the dashboard
        Then the cancellation is accepted
