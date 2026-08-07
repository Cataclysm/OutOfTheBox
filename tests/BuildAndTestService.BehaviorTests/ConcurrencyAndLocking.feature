Feature: Concurrency and Locking
    Mirrors specs/dotnet-command-execution's "Commands against different repos run in parallel"
    and "One in-flight command per repo" requirements.

    Scenario: Commands against different repos run in parallel
        When authenticated runs are started concurrently against "PassingFixture" and "FailingFixture"
        Then both concurrent runs complete independently

    Scenario: A second request for a busy repo is rejected
        Given an in-flight run against "HangingFixture"
        When a second authenticated run is started against "HangingFixture"
        Then the second run is rejected identifying the in-flight run's id

    Scenario: The repo becomes available again once the in-flight run ends
        Given an in-flight run against "HangingFixture" with a 3 second timeout
        When that run reaches a terminal state
        And a second authenticated run is started against "HangingFixture" with a 3 second timeout
        Then the second run is accepted
