Feature: Cancellation API
    Mirrors specs/dotnet-command-execution's "Caller can cancel an in-flight command" and
    "Cancelling an unknown or finished run" scenarios.

    Scenario: Cancelling an in-flight run kills it and frees the repo
        Given a cancellable in-flight run against "HangingFixture"
        When that run is cancelled
        Then the cancel request is accepted
        And the run's stream ends with reason "cancelled"
        And a subsequent run against "HangingFixture" is accepted

    Scenario: Cancelling an unknown run id is rejected
        When a cancel request is sent for an unknown run id
        Then the cancel request is rejected as not found

    Scenario: Cancelling an already-completed run is rejected
        Given a run has already completed against "PassingFixture"
        When that run is cancelled
        Then the cancel request is rejected as not found

    Scenario: Cancelling the same run twice only has effect once
        Given a cancellable in-flight run against "HangingFixture"
        When that run is cancelled
        Then the cancel request is accepted
        And the run's stream ends with reason "cancelled"
        When that run is cancelled again
        Then the cancel request is rejected as not found
