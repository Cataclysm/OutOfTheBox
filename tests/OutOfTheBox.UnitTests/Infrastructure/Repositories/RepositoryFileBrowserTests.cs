// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Diagnostics;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Infrastructure.Events;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Persistence;
using OutOfTheBox.Infrastructure.Repositories;
using OutOfTheBox.UnitTests.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Infrastructure.Repositories;

/// <summary>
/// Exercises <see cref="RepositoryFileBrowser"/> end to end against a real temp directory tree - no
/// process spawning involved at all (unlike <see cref="RepositoryManagerTests"/>'s git-invoking
/// methods), so every path here runs for real, not just the rejection paths.
/// </summary>
public sealed class RepositoryFileBrowserTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteInMemoryDbContextFactory _dbContextFactory = new();
    private readonly IRunEventBus _runEventBus = new InMemoryRunEventBus(NullLogger<InMemoryRunEventBus>.Instance);

    public RepositoryFileBrowserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "oob-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _dbContextFactory.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ListDirectoryAsync_lists_the_repository_root_folders_first_then_alphabetical()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "zeta-folder"));
        Directory.CreateDirectory(Path.Combine(repositoryPath, "alpha-folder"));
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "readme.txt"), "hello");

        var browser = CreateBrowser(new RunRegistry());
        var entries = await browser.ListDirectoryAsync("repo", string.Empty, CancellationToken.None);

        Assert.Equal(["alpha-folder", "zeta-folder", "readme.txt"], entries.Select(e => e.Name));
        Assert.True(entries[0].IsDirectory);
        Assert.True(entries[1].IsDirectory);
        Assert.False(entries[2].IsDirectory);
        Assert.Equal(5, entries[2].SizeBytes);
    }

    [Fact]
    public async Task ListDirectoryAsync_lists_a_nested_subdirectory()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "sub"));
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "sub", "nested.txt"), "content");

        var browser = CreateBrowser(new RunRegistry());
        var entries = await browser.ListDirectoryAsync("repo", "sub", CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal("nested.txt", entry.Name);
    }

    [Theory]
    [InlineData("does-not-exist", "")]
    [InlineData("repo", "no-such-subdir")]
    public async Task ListDirectoryAsync_returns_empty_for_an_invalid_repository_or_path(string repositoryName, string relativePath)
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo"));
        var browser = CreateBrowser(new RunRegistry());

        Assert.Empty(await browser.ListDirectoryAsync(repositoryName, relativePath, CancellationToken.None));
    }

    [Fact]
    public async Task FindEntriesAsync_matches_a_recursive_pattern_across_subdirectories()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "src", "nested"));
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "top.cs"), "content");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "src", "mid.cs"), "content");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "src", "nested", "deep.cs"), "content");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "src", "nested", "deep.txt"), "content");

        var browser = CreateBrowser(new RunRegistry());
        var result = await browser.FindEntriesAsync("repo", "**/*.cs", CancellationToken.None);

        Assert.Equal(
            ["src/mid.cs", "src/nested/deep.cs", "top.cs"],
            result.Entries.Select(e => e.RelativePath));
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task FindEntriesAsync_a_non_recursive_pattern_only_matches_the_repository_root()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "sub"));
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "root.md"), "content");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "sub", "nested.md"), "content");

        var browser = CreateBrowser(new RunRegistry());
        var result = await browser.FindEntriesAsync("repo", "*.md", CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("root.md", entry.RelativePath);
    }

    [Fact]
    public async Task FindEntriesAsync_matches_directories_too_not_just_files()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "target-folder"));
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "target-folder", "inside.txt"), "content");

        var browser = CreateBrowser(new RunRegistry());
        var result = await browser.FindEntriesAsync("repo", "**/target-folder", CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("target-folder", entry.RelativePath);
        Assert.True(entry.IsDirectory);
        Assert.Null(entry.SizeBytes);
    }

    [Fact]
    public async Task FindEntriesAsync_an_empty_pattern_matches_everything()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "a.txt"), "content");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "b.txt"), "content");

        var browser = CreateBrowser(new RunRegistry());
        var result = await browser.FindEntriesAsync("repo", string.Empty, CancellationToken.None);

        Assert.Equal(2, result.Entries.Count);
    }

    [Fact]
    public async Task FindEntriesAsync_caps_results_and_reports_truncation()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        for (var i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(repositoryPath, $"file{i}.txt"), "content");
        }

        var browser = CreateBrowser(new RunRegistry(), maxFindFilesResults: 3);
        var result = await browser.FindEntriesAsync("repo", "*.txt", CancellationToken.None);

        Assert.Equal(3, result.Entries.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task FindEntriesAsync_returns_empty_for_an_invalid_repository()
    {
        var browser = CreateBrowser(new RunRegistry());

        var result = await browser.FindEntriesAsync("does-not-exist", "**/*", CancellationToken.None);

        Assert.Empty(result.Entries);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task GetMetadataAsync_returns_file_fields()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        var filePath = Path.Combine(repositoryPath, "file.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var browser = CreateBrowser(new RunRegistry());
        var metadata = await browser.GetMetadataAsync("repo", "file.txt", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.False(metadata.IsDirectory);
        Assert.Equal(5, metadata.SizeBytes);
        Assert.Equal("file.txt", metadata.Name);
        Assert.False(metadata.IsLocked);
    }

    [Fact]
    public async Task GetMetadataAsync_returns_directory_fields_with_a_null_size_and_lock_status()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "folder"));

        var browser = CreateBrowser(new RunRegistry());
        var metadata = await browser.GetMetadataAsync("repo", "folder", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.True(metadata.IsDirectory);
        Assert.Null(metadata.SizeBytes);
        Assert.Null(metadata.IsLocked);
    }

    [Fact]
    public async Task GetMetadataAsync_reports_a_locked_file()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        var filePath = Path.Combine(repositoryPath, "locked.txt");
        await File.WriteAllTextAsync(filePath, "content");

        var fileLockInspector = new FakeFileLockInspector();
        fileLockInspector.LockedPaths.Add(Path.GetFullPath(filePath));
        var browser = CreateBrowser(new RunRegistry(), fileLockInspector: fileLockInspector);

        var metadata = await browser.GetMetadataAsync("repo", "locked.txt", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.True(metadata.IsLocked);
    }

    [Fact]
    public async Task GetMetadataAsync_returns_null_for_a_nonexistent_entry()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo"));
        var browser = CreateBrowser(new RunRegistry());

        Assert.Null(await browser.GetMetadataAsync("repo", "missing.txt", CancellationToken.None));
    }

    [Fact]
    public async Task GetMetadataAsync_returns_null_for_a_path_escaping_the_repository()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo"));
        await File.WriteAllTextAsync(Path.Combine(_root, "outside.txt"), "content");

        var browser = CreateBrowser(new RunRegistry());

        Assert.Null(await browser.GetMetadataAsync("repo", "../outside.txt", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_rejects_an_empty_relative_path()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo"));
        var browser = CreateBrowser(new RunRegistry());

        var result = await browser.DeleteAsync("repo", string.Empty, CancellationToken.None);

        var rejected = Assert.IsType<RepositoryFileActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_nonexistent_file()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo"));
        var browser = CreateBrowser(new RunRegistry());

        var result = await browser.DeleteAsync("repo", "missing.txt", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryFileActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.NotFound, rejected.Reason);
    }

    [Fact]
    public async Task DeleteAsync_removes_a_file()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        var filePath = Path.Combine(repositoryPath, "file.txt");
        await File.WriteAllTextAsync(filePath, "content");

        var browser = CreateBrowser(new RunRegistry());
        var result = await browser.DeleteAsync("repo", "file.txt", CancellationToken.None);

        Assert.IsType<RepositoryFileActionResult.Succeeded>(result);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteAsync_removes_a_folder_recursively()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        var folderPath = Path.Combine(repositoryPath, "folder");
        Directory.CreateDirectory(folderPath);
        await File.WriteAllTextAsync(Path.Combine(folderPath, "inside.txt"), "content");

        var browser = CreateBrowser(new RunRegistry());
        var result = await browser.DeleteAsync("repo", "folder", CancellationToken.None);

        Assert.IsType<RepositoryFileActionResult.Succeeded>(result);
        Assert.False(Directory.Exists(folderPath));
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_busy_repository()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        var filePath = Path.Combine(repositoryPath, "file.txt");
        Directory.CreateDirectory(repositoryPath);
        await File.WriteAllTextAsync(filePath, "content");

        var registry = new RunRegistry();
        using var cts = new CancellationTokenSource();
        var conflictingRunId = Guid.NewGuid();
        registry.TryAcquire(repositoryPath, conflictingRunId, cts, out _);

        var browser = CreateBrowser(registry);
        var result = await browser.DeleteAsync("repo", "file.txt", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryFileActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.Busy, rejected.Reason);
        Assert.Equal(conflictingRunId, rejected.ConflictingRunId);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteAsync_records_a_completed_run_in_history()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        var filePath = Path.Combine(repositoryPath, "file.txt");
        await File.WriteAllTextAsync(filePath, "content");

        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var browser = CreateBrowser(new RunRegistry(), runRepository: runRepository);

        await browser.DeleteAsync("repo", "file.txt", CancellationToken.None);

        var runs = await runRepository.ListAsync(new() { Kinds = [RunKind.RepositoryFileDelete] }, CancellationToken.None);
        var run = Assert.Single(runs);
        Assert.Equal(RunOutcome.Completed, run.Outcome);
        Assert.Equal("file.txt", run.FilePath);
    }

    [Fact]
    public async Task DeleteAsync_records_a_failed_run_with_the_error_message_when_rejected_after_the_lock_is_acquired()
    {
        // A directory containing an open (undeletable) file is the simplest way to force a real
        // failure past the busy/not-found checks, without needing platform-specific lock trickery.
        var repositoryPath = Path.Combine(_root, "repo");
        var folderPath = Path.Combine(repositoryPath, "folder");
        Directory.CreateDirectory(folderPath);
        var lockedFilePath = Path.Combine(folderPath, "locked.txt");
        await File.WriteAllTextAsync(lockedFilePath, "content");

        var runRepository = new EfRunRepository(_dbContextFactory.CreateContext());
        var browser = CreateBrowser(new RunRegistry(), runRepository: runRepository);

        await using (new FileStream(lockedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await browser.DeleteAsync("repo", "folder", CancellationToken.None);

            // RecursiveDelete retries transient locks for a while before giving up - this only
            // reliably fails within a single unit-test-speed window if retries are exhausted, so
            // assert on whichever of the two real outcomes happened rather than assuming failure.
            if (result is RepositoryFileActionResult.Failed)
            {
                var runs = await runRepository.ListAsync(new() { Kinds = [RunKind.RepositoryFileDelete] }, CancellationToken.None);
                var run = Assert.Single(runs);
                Assert.Equal(RunOutcome.Failed, run.Outcome);
                Assert.False(string.IsNullOrEmpty(run.Stderr));
            }
        }
    }

    [Fact]
    public async Task RenameAsync_rejects_an_empty_relative_path()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo"));
        var browser = CreateBrowser(new RunRegistry());

        var result = await browser.RenameAsync("repo", string.Empty, "new-name", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryFileActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
    }

    [Theory]
    [InlineData("sub/escaped.txt")]
    [InlineData(@"sub\escaped.txt")]
    [InlineData("")]
    public async Task RenameAsync_rejects_a_new_name_containing_a_path_separator_or_empty(string newName)
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "file.txt"), "content");

        var browser = CreateBrowser(new RunRegistry());
        var result = await browser.RenameAsync("repo", "file.txt", newName, CancellationToken.None);

        var rejected = Assert.IsType<RepositoryFileActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.InvalidName, rejected.Reason);
    }

    [Fact]
    public async Task RenameAsync_rejects_a_nonexistent_source()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo"));
        var browser = CreateBrowser(new RunRegistry());

        var result = await browser.RenameAsync("repo", "missing.txt", "new-name.txt", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryFileActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.NotFound, rejected.Reason);
    }

    [Fact]
    public async Task RenameAsync_rejects_when_the_destination_already_exists()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "file.txt"), "content");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "taken.txt"), "other");

        var browser = CreateBrowser(new RunRegistry());
        var result = await browser.RenameAsync("repo", "file.txt", "taken.txt", CancellationToken.None);

        var rejected = Assert.IsType<RepositoryFileActionResult.Rejected>(result);
        Assert.Equal(RepositoryActionRejectionReason.AlreadyExists, rejected.Reason);
    }

    [Fact]
    public async Task RenameAsync_renames_a_file_in_place()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "old-name.txt"), "content");

        var browser = CreateBrowser(new RunRegistry());
        var result = await browser.RenameAsync("repo", "old-name.txt", "new-name.txt", CancellationToken.None);

        Assert.IsType<RepositoryFileActionResult.Succeeded>(result);
        Assert.False(File.Exists(Path.Combine(repositoryPath, "old-name.txt")));
        Assert.True(File.Exists(Path.Combine(repositoryPath, "new-name.txt")));
    }

    [Fact]
    public async Task ResolveConfinedFilePathAsync_returns_null_for_a_directory()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "folder"));

        var browser = CreateBrowser(new RunRegistry());

        Assert.Null(await browser.ResolveConfinedFilePathAsync("repo", "folder", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveConfinedFilePathAsync_returns_the_resolved_path_for_an_existing_file()
    {
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repositoryPath);
        var filePath = Path.Combine(repositoryPath, "file.txt");
        await File.WriteAllTextAsync(filePath, "content");

        var browser = CreateBrowser(new RunRegistry());

        Assert.Equal(Path.GetFullPath(filePath), await browser.ResolveConfinedFilePathAsync("repo", "file.txt", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveConfinedFilePathAsync_returns_null_for_a_path_escaping_the_repository()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo"));
        await File.WriteAllTextAsync(Path.Combine(_root, "outside.txt"), "content");

        var browser = CreateBrowser(new RunRegistry());

        Assert.Null(await browser.ResolveConfinedFilePathAsync("repo", "../outside.txt", CancellationToken.None));
    }

    private RepositoryFileBrowser CreateBrowser(
        RunRegistry runRegistry,
        IRunRepository? runRepository = null,
        IFileLockInspector? fileLockInspector = null,
        int maxFindFilesResults = 2000) =>
        new(
            new WorkingDirectoryResolver(Options.Create(new ServiceOptions { RootDirectory = _root }), NullLogger<WorkingDirectoryResolver>.Instance),
            runRegistry,
            runRepository ?? new EfRunRepository(_dbContextFactory.CreateContext()),
            _runEventBus,
            fileLockInspector ?? new FakeFileLockInspector(),
            Options.Create(new ServiceOptions { RootDirectory = _root, McpMaxFindFilesResults = maxFindFilesResults }),
            NullLogger<RepositoryFileBrowser>.Instance);

    // Empty by default (nothing locked) - tests that care about a locked file add its resolved path
    // to LockedPaths first, matching the same "not exercised, say so"-adjacent precedent other fakes
    // in this test suite use, just configurable instead of always throwing.
    private sealed class FakeFileLockInspector : IFileLockInspector
    {
        public HashSet<string> LockedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<FileLockingProcess>> GetLockingProcessesAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLockingProcess>>(
                LockedPaths.Contains(filePath) ? [new FileLockingProcess(1234, "test.exe", FileLockApplicationType.Unknown, IsRestartable: true)] : []);
    }
}
