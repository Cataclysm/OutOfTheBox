using BuildAndTestService.Application.Configuration;
using BuildAndTestService.Infrastructure.Execution;
using Microsoft.Extensions.Options;

namespace BuildAndTestService.UnitTests.Infrastructure.Execution;

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
        new(Options.Create(new ServiceOptions { RootDirectory = _root }));

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
}
