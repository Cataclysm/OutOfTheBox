// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Infrastructure.Execution;
using OutOfTheBox.Infrastructure.Repositories;
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

    public RepositoryFileBrowserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "oob-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
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

    private RepositoryFileBrowser CreateBrowser(RunRegistry runRegistry) =>
        new(new WorkingDirectoryResolver(Options.Create(new ServiceOptions { RootDirectory = _root })), runRegistry);
}
