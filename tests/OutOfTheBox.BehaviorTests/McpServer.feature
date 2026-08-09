Feature: MCP Server
    Mirrors specs/mcp-server: hosting, authentication, and tool discovery for the MCP Streamable HTTP
    endpoint - the foundation every tool defined by mcp-command-execution/mcp-file-transfer/
    mcp-repository-access builds on. Driven over real HTTP against a real running instance of the
    service (Host, via WebApplicationFactory), the same way RepositoryRestEndpoints.feature covers
    the REST API's own HTTP contract.

    Scenario: Successful initialization
        When an authenticated caller completes the MCP initialize handshake
        Then the handshake succeeds

    Scenario: Missing bearer token
        When an unauthenticated caller sends an MCP request
        Then the MCP response is unauthorized

    Scenario: Invalid bearer token
        When a caller presents an invalid bearer token to the MCP endpoint
        Then the MCP response is unauthorized

    Scenario: Listing available tools
        When an authenticated caller lists MCP tools
        Then the tool list contains exactly "dotnet_run, git_run, read_run_output, cancel_run, transfer_file, list_repositories, clone_repository, get_run_resources, get_environment_info, get_file_lock_info"

    Scenario: Unknown tool name
        When an authenticated caller calls the unknown MCP tool "delete_repository"
        Then the MCP call is rejected as an unknown tool

    Scenario: Arguments fail schema validation
        When an authenticated caller calls "dotnet_run" with missing required arguments
        Then the MCP call is rejected without starting a run
