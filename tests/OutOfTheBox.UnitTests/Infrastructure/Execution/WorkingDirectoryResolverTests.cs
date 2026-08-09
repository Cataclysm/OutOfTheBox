// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Infrastructure.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Infrastructure.Execution;

/// <summary>
/// Exercises <see cref="WorkingDirectoryResolver"/> against a real, throwaway directory tree
/// under the OS temp folder - path canonicalization and symlink resolution are genuine IO, not
/// something worth faking here.
/// </summary>
public sealed class WorkingDirectoryResolverTests : IDisposable
{
    private readonly string _root;

    public WorkingDirectoryResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bts-tests", Guid.NewGuid().ToString("N"), "root");
        Directory.CreateDirectory(Path.Combine(_root, "myrepo", "src"));
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(_root)!, "root-evil", "myrepo"));

        // For the two-level (root -> repository -> file) confinement composition tests, per
        // specs/file-transfer: a second repository under the same root, so a traversal from within
        // "myrepo" that still resolves under the root but outside "myrepo" specifically can be
        // exercised (a single-level, root-only check would wrongly allow it).
        Directory.CreateDirectory(Path.Combine(_root, "other-repository"));
        File.WriteAllText(Path.Combine(_root, "other-repository", "secret.txt"), "secret");
        File.WriteAllText(Path.Combine(_root, "myrepo", "src", "app.dll"), "binary-content");
    }

    public void Dispose()
    {
        var testRunDirectory = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(testRunDirectory))
        {
            Directory.Delete(testRunDirectory, recursive: true);
        }
    }

    private WorkingDirectoryResolver CreateResolver() =>
        new(Options.Create(new ServiceOptions { RootDirectory = _root }), NullLogger<WorkingDirectoryResolver>.Instance);

    [Fact]
    public void Resolve_allows_a_valid_subdirectory()
    {
        var result = CreateResolver().Resolve(Path.Combine("myrepo", "src"));

        Assert.True(result.IsAllowed);
        Assert.Equal(Path.Combine(_root, "myrepo", "src"), result.ResolvedPath);
    }

    [Fact]
    public void Resolve_rejects_a_traversal_attempt()
    {
        var result = CreateResolver().Resolve(Path.Combine("..", "..", "Windows", "System32"));

        Assert.False(result.IsAllowed);
        Assert.Null(result.ResolvedPath);
    }

    [Fact]
    public void Resolve_rejects_an_absolute_path_outside_the_root()
    {
        var result = CreateResolver().Resolve(@"C:\Windows\System32");

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Resolve_rejects_a_sibling_directory_sharing_a_name_prefix()
    {
        // Root is "<tmp>\root"; this targets "<tmp>\root-evil\myrepo" via a relative escape.
        var result = CreateResolver().Resolve(Path.Combine("..", "root-evil", "myrepo"));

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Resolve_follows_a_symlink_that_escapes_the_root()
    {
        var linkPath = Path.Combine(_root, "escape-link");
        var outsideTarget = Path.Combine(Path.GetDirectoryName(_root)!, "root-evil");

        try
        {
            Directory.CreateSymbolicLink(linkPath, outsideTarget);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Creating a symlink requires elevated privileges or Developer Mode on Windows;
            // skip rather than fail the whole suite in an environment without that privilege.
            return;
        }

        var result = CreateResolver().Resolve("escape-link");

        Assert.False(result.IsAllowed);
    }

    // The following exercise the two-level composition specs/file-transfer relies on: first
    // resolve the repository against the configured root (Resolve, unchanged), then resolve the file
    // path against that *specific resolved repository directory* (ResolveWithinRoot again) - no new
    // Domain/confinement logic, just a second call site for the same primitive.

    [Fact]
    public void ResolveWithinRoot_composed_twice_accepts_a_genuinely_nested_valid_path()
    {
        var resolver = CreateResolver();
        var repository = resolver.Resolve("myrepo");
        Assert.True(repository.IsAllowed);

        var file = resolver.ResolveWithinRoot(repository.ResolvedPath!, Path.Combine("src", "app.dll"));

        Assert.True(file.IsAllowed);
        Assert.Equal(Path.Combine(_root, "myrepo", "src", "app.dll"), file.ResolvedPath);
    }

    [Fact]
    public void ResolveWithinRoot_composed_twice_rejects_a_sibling_repo_path()
    {
        // "other-repository" is itself a valid path under the root - a single-level, root-only check
        // would wrongly allow this; confinement to "myrepo" specifically must reject it.
        var resolver = CreateResolver();
        var repository = resolver.Resolve("myrepo");
        Assert.True(repository.IsAllowed);

        var file = resolver.ResolveWithinRoot(repository.ResolvedPath!, Path.Combine("..", "other-repository", "secret.txt"));

        Assert.False(file.IsAllowed);
    }

    [Fact]
    public void ResolveWithinRoot_composed_twice_rejects_traversal_within_the_named_repo()
    {
        var resolver = CreateResolver();
        var repository = resolver.Resolve("myrepo");
        Assert.True(repository.IsAllowed);

        var file = resolver.ResolveWithinRoot(repository.ResolvedPath!, Path.Combine("..", "..", "Windows", "System32"));

        Assert.False(file.IsAllowed);
    }

    [Fact]
    public void ResolveWithinRoot_composed_twice_rejects_an_absolute_path()
    {
        var resolver = CreateResolver();
        var repository = resolver.Resolve("myrepo");
        Assert.True(repository.IsAllowed);

        var file = resolver.ResolveWithinRoot(repository.ResolvedPath!, @"C:\Windows\System32");

        Assert.False(file.IsAllowed);
    }

    [Fact]
    public void ResolveWithinRoot_composed_twice_follows_a_symlink_that_escapes_the_named_repo()
    {
        // Per specs/file-transfer's "Path escapes via a symlink" scenario. Unit-level only,
        // matching how Resolve's own symlink-escape case (above) is unit-tested rather than
        // exercised through a full BDD/HTTP scenario - this composition reuses the exact same
        // ResolveSymlinkTarget/PathConfinementPolicy machinery, just at the second confinement level.
        var resolver = CreateResolver();
        var repository = resolver.Resolve("myrepo");
        Assert.True(repository.IsAllowed);

        var linkPath = Path.Combine(repository.ResolvedPath!, "escape-link");
        var outsideTarget = Path.Combine(_root, "other-repository");

        try
        {
            Directory.CreateSymbolicLink(linkPath, outsideTarget);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Creating a symlink requires elevated privileges or Developer Mode on Windows; skip
            // rather than fail the whole suite in an environment without that privilege.
            return;
        }

        var file = resolver.ResolveWithinRoot(repository.ResolvedPath!, "escape-link");

        Assert.False(file.IsAllowed);
    }
}
