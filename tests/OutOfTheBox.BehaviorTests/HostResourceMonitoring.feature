Feature: Host Resource Monitoring
    Mirrors specs/host-resource-monitoring/spec.md, driven against a real running instance of the
    service (Host, via WebApplicationFactory), sampling real spawned processes - the same "real
    dotnet.exe spawns" approach the rest of this project's BehaviorTests already use, since this is
    exactly the behavior under test.

    Scenario: A run's spawned children appear in its process sublist
        Given a dotnet run is in flight against a long-running fixture
        When the resource sampler ticks
        Then that run's process sublist includes its spawned process with plausible CPU and RAM values

    Scenario: Killing a listed process terminates it and its descendants
        Given a dotnet run is in flight against a long-running fixture
        And the resource sampler ticks
        When the operator kills that run's root process
        Then the kill is accepted
        And the run's process sublist is empty on the next tick

    Scenario: The process list is empty when nothing is in flight
        When the resource sampler ticks with nothing in flight
        Then no run has a process sublist

    Scenario: An artifact transfer never gets a process sublist
        Given a transfer is registered as in flight
        When the resource sampler ticks
        Then the transfer has no process sublist
