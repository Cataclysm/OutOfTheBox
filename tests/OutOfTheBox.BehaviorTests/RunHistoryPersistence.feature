Feature: Run History Persistence
    Mirrors specs/run-history/spec.md's restart-durability requirement: a run's persisted history
    record survives the service process restarting. Driven against a real running instance of the
    service (Host, via WebApplicationFactory), restarted by disposing the first factory instance
    and creating a second one pointed at the same SQLite file - there is no dashboard/REST surface
    for run history yet (Section 12), so the assertion reads the persisted row directly through
    IRunRepository, the same way the future dashboard will.

    Scenario: A completed dotnet run's history record survives a service restart
        When a dotnet run completes against "PassingFixture"
        And the service is restarted
        Then the persisted history record for that run shows outcome "Completed"

    Scenario: A completed git run's history record survives a service restart
        When a git run completes against the git fixture
        And the service is restarted
        Then the persisted history record for that run shows outcome "Completed"

    Scenario: A completed transfer's history record survives a service restart
        When a transfer of "SampleTests.cs" from "PassingFixture" completes
        And the service is restarted
        Then the persisted history record for that run shows outcome "Completed"
        And it has a recorded artifact size
