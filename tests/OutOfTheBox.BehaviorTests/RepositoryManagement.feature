Feature: Repository Management
    Mirrors specs/repository-management/spec.md - full repository management (delete, pull/push/
    force-push/fetch/clean, branch switching, commit checkout, the file tree browser) is
    dashboard-only, not reachable via MCP (mcp-repository-access deliberately covers only
    list/clone). These scenarios call IRepositoryManager directly from a resolved DI
    scope - the same way Blazor component code-behind would - rather than through HTTP, against a
    real local git source (reusing GitFixture, the same fixture used elsewhere) so `git clone`
    genuinely runs, not a network-dependent remote URL.

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

    Scenario: Cancelling a clone from the dashboard
        Given a clone into a given name is already in flight
        When an operator cancels that clone from the dashboard
        Then the cancellation is accepted

    Scenario: Viewing the diff for a file changed by a commit
        When an operator requests the diff for "README.md" in the fixture repository's initial commit
        Then the diff shows "README.md" as the changed file with an added "GitFixture" line

    Scenario: Requesting a diff for a path the commit didn't touch returns nothing
        When an operator requests the diff for "does-not-exist.txt" in the fixture repository's initial commit
        Then no diff is returned

    Scenario: A commit's changed files include their added/removed line counts
        When an operator requests the commit detail for the fixture repository's initial commit
        Then the changed file "README.md" shows 1 line added and 0 lines removed
