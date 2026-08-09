Feature: MCP Resource Monitoring
    Mirrors specs/mcp-resource-monitoring/spec.md, driven against a real running instance of the
    service (Host, via WebApplicationFactory) with a real dotnet.exe child process, so
    get_run_resources reflects genuinely observed CPU/RAM rather than fabricated figures. Forces a
    deterministic sample via HostResourceSamplerService's own public TickAsync (see its own remarks)
    instead of waiting on its real ~3-second PeriodicTimer.

    Scenario: Polling an in-flight run with active samples
        Given a dotnet_run is in flight against a long-running fixture
        And the resource sampler persists a sample for that run
        When an authenticated caller calls get_run_resources for that run
        Then the result includes at least one sample point and a trend summary

    Scenario: Polling immediately after starting a run returns an empty result
        Given a dotnet_run is in flight against a long-running fixture
        When an authenticated caller calls get_run_resources for that run before any sampler tick
        Then the result has no sample points and no trend summary

    Scenario: get_run_resources on an unknown run id is rejected
        When an authenticated caller calls get_run_resources with an unknown run id
        Then the get_run_resources call is rejected
