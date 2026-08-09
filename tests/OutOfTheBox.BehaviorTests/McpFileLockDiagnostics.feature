Feature: MCP File Lock Diagnostics
    Mirrors specs/mcp-file-lock-diagnostics/spec.md, driven against a real running instance of the
    service (Host, via WebApplicationFactory) with a genuinely locked file, so get_file_lock_info
    reflects a real Restart Manager query rather than a fabricated result.

    Scenario: A file locked by another process
        Given a file inside a repository is locked open
        When an authenticated caller calls get_file_lock_info for that file
        Then the result lists this test process as a locking process

    Scenario: A file with no lock
        When an authenticated caller calls get_file_lock_info for an unlocked file
        Then the result has no locking processes

    Scenario: Path escapes the named repository
        When an authenticated caller calls get_file_lock_info with a path that escapes the repository
        Then the get_file_lock_info call is rejected

    Scenario: File does not exist
        When an authenticated caller calls get_file_lock_info for a file that does not exist
        Then the get_file_lock_info call is rejected
