Feature: MCP File Management
    Mirrors specs/mcp-file-management/spec.md, driven against a real running instance of the service
    (Host, via WebApplicationFactory) pointed at a freshly-generated scratch repository.

    Scenario: Finding files by extension anywhere in the repository
        Given a repository with nested files at "src/top.cs"
        And a repository with nested files at "src/sub/deep.cs"
        And a repository with nested files at "src/sub/deep.txt"
        When an authenticated caller calls find_files with pattern "**/*.cs"
        Then the find_files result lists exactly "src/top.cs, src/sub/deep.cs"

    Scenario: A non-recursive pattern only matches one directory level
        Given a repository with nested files at "root.md"
        And a repository with nested files at "sub/nested.md"
        When an authenticated caller calls find_files with pattern "*.md"
        Then the find_files result lists exactly "root.md"

    Scenario: The pattern matches directories too, not just files
        Given a repository with nested files at "target-folder/inside.txt"
        When an authenticated caller calls find_files with pattern "**/target-folder"
        Then the find_files result lists exactly "target-folder"
        And the matched entry "target-folder" is a directory

    Scenario: No pattern supplied lists everything
        Given a repository with nested files at "a.txt"
        And a repository with nested files at "sub/b.txt"
        When an authenticated caller calls find_files with no pattern
        Then the find_files result includes "a.txt, sub/b.txt"

    Scenario: A pattern matches more entries than the configured cap
        Given a repository with 5 files matching "*.txt"
        And the configured MCP find_files result cap is 3
        When an authenticated caller calls find_files with pattern "*.txt"
        Then the find_files result has exactly 3 entries and is marked truncated

    Scenario: Metadata for an existing file
        Given a repository with nested files at "file.txt"
        When an authenticated caller calls get_file_info for "file.txt"
        Then the get_file_info result reports a file with a size and owner

    Scenario: Metadata for a directory
        Given a repository with nested files at "folder/inside.txt"
        When an authenticated caller calls get_file_info for "folder"
        Then the get_file_info result reports a directory with no size and no lock status

    Scenario: get_file_info path escapes the named repository
        Given a repository with nested files at "file.txt"
        When an authenticated caller calls get_file_info for "../outside.txt"
        Then the get_file_info call is rejected as a confinement violation

    Scenario: get_file_info target does not exist
        Given a repository with nested files at "file.txt"
        When an authenticated caller calls get_file_info for "does-not-exist.txt"
        Then the get_file_info call is rejected as not found

    Scenario: Deleting an existing file
        Given a repository with nested files at "file.txt"
        When an authenticated caller calls delete_path for "file.txt"
        Then delete_path reports success and "file.txt" no longer exists on disk

    Scenario: Deleting an existing directory
        Given a repository with nested files at "folder/inside.txt"
        When an authenticated caller calls delete_path for "folder"
        Then delete_path reports success and "folder" no longer exists on disk

    Scenario: delete_path rejects an empty path
        Given a repository with nested files at "file.txt"
        When an authenticated caller calls delete_path for ""
        Then the delete_path call is rejected

    Scenario: delete_path path escapes the named repository
        Given a repository with nested files at "file.txt"
        When an authenticated caller calls delete_path for "../outside.txt"
        Then the delete_path call is rejected as a confinement violation

    Scenario: delete_path target does not exist
        Given a repository with nested files at "file.txt"
        When an authenticated caller calls delete_path for "does-not-exist.txt"
        Then the delete_path call is rejected as not found

    Scenario: Every delete_path call is recorded in run history
        Given a repository with nested files at "file.txt"
        When an authenticated caller calls delete_path for "file.txt"
        Then a RepositoryFileDelete run appears in history with outcome "Completed"
