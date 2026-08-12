Feature: MCP Git Credentials
    Mirrors specs/mcp-git-credentials/spec.md, driven against a real running instance of the service
    (Host, via WebApplicationFactory) using a throwaway, file-based git credential helper (see
    TestGitCredentialConfigSetup) - never the real developer/CI machine's global git config or
    Windows Credential Manager.

    Scenario: Authorizing a new host
        When an authenticated caller calls authorize_git_host for "github.example.test" with a token
        Then authorize_git_host reports success

    Scenario: An authorized host appears via list_authorized_git_hosts
        Given a host "github.example.test" has been authorized with a token
        When an authenticated caller calls list_authorized_git_hosts
        Then the result includes "github.example.test" with an authorization timestamp and no token value

    Scenario: No hosts authorized yet
        When an authenticated caller calls list_authorized_git_hosts
        Then the result is an empty list

    Scenario: Re-authorizing an already-authorized host replaces the credential
        Given a host "github.example.test" has been authorized with a token
        When an authenticated caller calls authorize_git_host for "github.example.test" with a different token
        Then authorize_git_host reports success
        And list_authorized_git_hosts lists "github.example.test" exactly once

    Scenario: Revoking an authorized host
        Given a host "github.example.test" has been authorized with a token
        When an authenticated caller calls revoke_git_host_authorization for "github.example.test"
        Then revoke_git_host_authorization reports success
        And "github.example.test" no longer appears via list_authorized_git_hosts

    Scenario: Revoking a host that was never authorized
        When an authenticated caller calls revoke_git_host_authorization for "never-authorized.example.test"
        Then the revoke_git_host_authorization call is rejected as nothing to revoke

    Scenario: The background credential sync service repairs a host's credential-helper entry after it is lost
        Given a host "github.example.test" has been authorized with a token
        When "github.example.test"'s credential-helper entry is deleted out of band
        And the background credential sync service runs a sync sweep
        Then git credential fill for "github.example.test" returns the originally authorized token
