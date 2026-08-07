Feature: Artifact Transfer
    Mirrors specs/artifact-transfer/spec.md, driven against a real running instance of the service
    (Host, via WebApplicationFactory) pointed at the checked-in fixture repos.

    Scenario: Successful transfer of a file a build produced
        Given a dotnet test run has produced "TestResults/results.trx" in "PassingFixture"
        When an authenticated caller transfers "TestResults/results.trx" from "PassingFixture"
        Then the transfer's run id is returned
        And the transferred bytes match the source file exactly

    Scenario: Path traversal is rejected
        When an authenticated caller transfers "../FailingFixture/SampleTests.cs" from "PassingFixture"
        Then the transfer is rejected as a confinement violation

    Scenario: An absolute path is rejected
        When an authenticated caller transfers "C:\Windows\System32\drivers\etc\hosts" from "PassingFixture"
        Then the transfer is rejected as a confinement violation

    Scenario: A missing file is distinguishable from a confinement violation
        When an authenticated caller transfers "does-not-exist.txt" from "PassingFixture"
        Then the transfer is rejected as not found

    Scenario: A transfer proceeds while a command is in flight against the same repo
        Given a command run is in flight against "HangingFixture"
        When an authenticated caller transfers "SampleTests.cs" from "HangingFixture"
        Then the transfer's run id is returned
        And the transferred bytes match the source file exactly

    Scenario: Cancelling an in-flight transfer stops it
        Given a large file "big.bin" exists in "PassingFixture"
        When an authenticated caller starts transferring "big.bin" from "PassingFixture"
        And that transfer is cancelled
        Then the transfer cancel request is accepted
        And fewer bytes were received than the file's full size
