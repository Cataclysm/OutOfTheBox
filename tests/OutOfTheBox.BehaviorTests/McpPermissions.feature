Feature: MCP Permissions
    Mirrors get_mcp_permissions - lets a caller see which MCP tools/subcommands are currently enabled
    via the operator-configurable MCP Settings dashboard page, driven against a real running instance
    of the service (Host, via WebApplicationFactory).

    Scenario: Listing current permissions reports every known key's actual current state
        Given the "delete_repository" tool is disabled in MCP Settings
        And the "dotnet:publish" tool is disabled in MCP Settings
        And the "find_files" tool is enabled in MCP Settings
        And the "dotnet:build" tool is enabled in MCP Settings
        When an authenticated caller calls get_mcp_permissions
        Then the result includes an entry for "find_files" that is enabled
        And the result includes an entry for "delete_repository" that is disabled
        And the result includes an entry for "dotnet:build" that is enabled
        And the result includes an entry for "dotnet:publish" that is disabled

    Scenario: A live permission change is reflected on the next call
        Given the "delete_path" tool is disabled in MCP Settings
        When an authenticated caller calls get_mcp_permissions
        Then the result includes an entry for "delete_path" that is disabled
        Given the "delete_path" tool is enabled in MCP Settings
        When an authenticated caller calls get_mcp_permissions
        Then the result includes an entry for "delete_path" that is enabled
