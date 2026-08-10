# mcp-file-management Specification

## Purpose
Lets an MCP caller find files/directories by glob pattern, inspect one filesystem entry's real metadata, and delete a file or directory - all confined to one named repository, and preferred over `git_run` for file-listing/metadata questions, which only reflect git's own tracked/index view of a repository, not its actual current filesystem state.
## Requirements
### Requirement: find_files searches a repository's real filesystem by glob pattern
The system SHALL accept a `find_files` tool call carrying a repository name and a glob pattern (supporting `*`, `**`, and `?`; defaulting to `**/*`, matching everything, if omitted), resolved with the same repository-level confinement every other repository-relative-path tool uses, and SHALL return every file and directory under that repository whose repository-relative path matches the pattern, each with its relative path, whether it's a directory, its size (files only), and last-modified time. The system SHALL prefer real, current filesystem state over anything `git` reports - untracked and `.gitignore`d files are included, since they are real files on disk regardless of git's own view of them.

#### Scenario: Finding files by extension anywhere in the repository
- **WHEN** an authenticated caller calls `find_files` for repository `X` with pattern `**/*.cs`
- **THEN** the result lists every `.cs` file anywhere under repository `X`'s directory tree, by its path relative to the repository root

#### Scenario: A non-recursive pattern only matches one directory level
- **WHEN** an authenticated caller calls `find_files` with a pattern containing no `**` segment (e.g. `*.md`)
- **THEN** the result includes only entries directly in the repository root, not files of that name nested in subdirectories

#### Scenario: The pattern matches directories too, not just files
- **WHEN** an authenticated caller calls `find_files` with a pattern that matches a directory's own relative path
- **THEN** that directory appears in the result, marked as a directory, alongside any matching files

#### Scenario: No pattern supplied lists everything
- **WHEN** an authenticated caller calls `find_files` for repository `X` without a pattern
- **THEN** the result lists every file and directory anywhere under repository `X`

### Requirement: Tool descriptions steer callers toward these tools, not git, for file information
The system SHALL describe `find_files` and `get_file_info` (in their MCP tool-discovery descriptions) as the preferred way to list files or inspect file metadata, in preference to running `git` commands via `git_run` for the same purpose - `git`'s own view (`git ls-files`, `git status`, etc.) reflects only tracked/index state, not the repository's actual current filesystem contents.

#### Scenario: Tool descriptions recommend themselves over git
- **WHEN** an authenticated caller lists available tools
- **THEN** `find_files`'s and `get_file_info`'s descriptions each state that they should be preferred over running git commands for file-listing or file-metadata questions

### Requirement: find_files results are capped with a visible truncation flag
The system SHALL cap the number of entries `find_files` returns at a configured maximum, and SHALL indicate in the result when the cap was reached, rather than silently returning an incomplete list indistinguishable from a complete one.

#### Scenario: A pattern matches more entries than the configured cap
- **WHEN** an authenticated caller calls `find_files` with a pattern matching more entries than the configured maximum
- **THEN** the result contains exactly the configured maximum number of entries and is marked as truncated

### Requirement: get_file_info returns one entry's real filesystem metadata
The system SHALL accept a `get_file_info` tool call carrying a repository name and a repository-relative path, resolved with the same two-level path confinement `transfer_file` already applies, and SHALL return that entry's type (file or directory), size (files only), filesystem attributes, created/last-modified/last-accessed timestamps, owner, and whether it is currently locked by another process (files only, reusing `mcp-file-lock-diagnostics`' own lock detection).

#### Scenario: Metadata for an existing file
- **WHEN** an authenticated caller calls `get_file_info` for a file that exists within the named repository
- **THEN** the result includes that file's size, attributes, timestamps, owner, and lock status

#### Scenario: Metadata for a directory
- **WHEN** an authenticated caller calls `get_file_info` for a directory that exists within the named repository
- **THEN** the result indicates it is a directory, with a `null` size and lock status, and its attributes/timestamps/owner still populated

#### Scenario: Path escapes the named repository or does not exist
- **WHEN** an authenticated caller calls `get_file_info` with a path that resolves outside the named repository, or that does not exist within it
- **THEN** the system rejects the call with a confinement or not-found error respectively, distinguishing the two

### Requirement: delete_path deletes a file or directory within a repository
The system SHALL accept a `delete_path` tool call carrying a repository name and a repository-relative path, resolved with the same two-level path confinement `transfer_file` already applies, and SHALL delete that file, or that directory and everything under it, applying the same per-repository lock and locked-file retry behavior the dashboard's own file tree browser delete action already uses. The system SHALL reject an empty path (deleting a repository itself is `delete_repository`'s own job, not this tool's).

#### Scenario: Deleting an existing file
- **WHEN** an authenticated caller calls `delete_path` for a file that exists within the named repository
- **THEN** the file is removed from disk and the tool call reports success

#### Scenario: Deleting an existing directory
- **WHEN** an authenticated caller calls `delete_path` for a directory that exists within the named repository
- **THEN** the directory and everything under it is removed from disk and the tool call reports success

#### Scenario: Path escapes the named repository, does not exist, or the repository is busy
- **WHEN** an authenticated caller calls `delete_path` with a path that resolves outside the named repository, that does not exist within it, or while the named repository has another run in flight
- **THEN** the system rejects the call with a confinement, not-found, or busy error respectively, distinguishing all three

#### Scenario: A deletion failure reports why, not just that it failed
- **WHEN** a `delete_path` call fails after being accepted (e.g. a file locked by another process the retry behavior couldn't clear in time)
- **THEN** the system reports the underlying error, not just a generic failure

#### Scenario: Every delete_path call is recorded in run history
- **WHEN** a `delete_path` call completes, successfully or with an error
- **THEN** it appears in run history with its repository, path, and outcome

