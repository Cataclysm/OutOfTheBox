Feature: MCP File Transfer
    Mirrors specs/mcp-file-transfer/spec.md, driven against a real running instance of the service
    (Host, via WebApplicationFactory) pointed at the checked-in fixture repositories.

    Scenario: Successful transfer
        When an authenticated caller calls transfer_file for "SampleTests.cs" in "PassingFixture"
        Then the transferred content matches the source file exactly

    Scenario: Path escapes the named repository
        When an authenticated caller calls transfer_file for "../FailingFixture/SampleTests.cs" in "PassingFixture"
        Then the transfer_file call is rejected as a confinement violation

    Scenario: File does not exist
        When an authenticated caller calls transfer_file for "does-not-exist.txt" in "PassingFixture"
        Then the transfer_file call is rejected as not found

    Scenario: A file exceeding the configured limit is rejected
        Given the configured MCP file transfer limit is 10 bytes
        When an authenticated caller calls transfer_file for "SampleTests.cs" in "PassingFixture"
        Then the transfer_file call is rejected as too large
