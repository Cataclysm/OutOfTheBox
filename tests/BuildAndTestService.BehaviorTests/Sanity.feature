Feature: Sanity
    A trivial scenario that exists only to prove the Reqnroll + xUnit toolchain works end-to-end via
    plain `dotnet test`, with no Visual Studio/IDE dependency. Not a real behavior scenario.

    Scenario: Basic arithmetic sanity check
        Given I have the number 2
        And I have the number 3
        When I add them together
        Then the result should be 5
