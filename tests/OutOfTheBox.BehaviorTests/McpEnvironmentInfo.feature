Feature: MCP Environment Info
    Mirrors specs/mcp-environment-info/spec.md, driven against a real running instance of the
    service (Host, via WebApplicationFactory) with this machine's real installed .NET/git toolchain,
    so get_environment_info reflects genuinely observed environment state rather than fabricated
    figures.

    Scenario: Retrieving environment info
        When an authenticated caller calls get_environment_info
        Then the result includes this host's real installed toolchain, SDKs, and disk space

    Scenario: Installed workloads never fail the call
        When an authenticated caller calls get_environment_info
        Then the call succeeds regardless of what the workload listing reports

    Scenario: The reported dotnet/git versions match the dashboard's own
        When an authenticated caller calls get_environment_info
        Then the reported dotnet and git versions match the dashboard's own installed-tool-versions provider
