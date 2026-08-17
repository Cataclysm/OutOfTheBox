// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Infrastructure.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.UnitTests.Infrastructure.Execution;

/// <summary>
/// Exercises <see cref="PathSanitizer"/> against a real <see cref="WorkingDirectoryResolver"/> and a
/// real, throwaway directory tree - the same "real IO, not worth faking" reasoning
/// <see cref="WorkingDirectoryResolverTests"/> already documents, since this type's whole job is
/// delegating to that resolver's genuine canonicalization/containment logic.
/// </summary>
public sealed class PathSanitizerTests : IDisposable
{
    private readonly string _root;
    private readonly PathSanitizer _sanitizer;

    public PathSanitizerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "oob-tests", Guid.NewGuid().ToString("N"), "myrepo");
        Directory.CreateDirectory(Path.Combine(_root, "out"));

        var resolver = new WorkingDirectoryResolver(
            Options.Create(new ServiceOptions { RootDirectory = Path.GetDirectoryName(_root)! }),
            NullLogger<WorkingDirectoryResolver>.Instance);
        _sanitizer = new PathSanitizer(resolver);
    }

    public void Dispose()
    {
        var testRunDirectory = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(testRunDirectory))
        {
            Directory.Delete(testRunDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("dotnet", new[] { "build" })]
    [InlineData("dotnet", new[] { "test", "--filter", "MyTests" })]
    [InlineData("dotnet", new[] { "restore" })]
    [InlineData("git", new[] { "status" })]
    [InlineData("git", new[] { "log", "--oneline", "-10" })]
    public void Validate_allows_arguments_with_no_path_bearing_flags(string executable, string[] arguments) =>
        Assert.Null(_sanitizer.Validate(executable, arguments, _root));

    [Theory]
    [InlineData("-o")]
    [InlineData("--output")]
    [InlineData("--results-directory")]
    public void Validate_rejects_a_dotnet_output_flag_escaping_the_repository(string flag) =>
        Assert.NotNull(_sanitizer.Validate("dotnet", ["test", flag, Path.Combine("..", "..", "escape")], _root));

    [Fact]
    public void Validate_allows_a_dotnet_output_flag_pointing_inside_the_repository() =>
        Assert.Null(_sanitizer.Validate("dotnet", ["build", "--output", "out"], _root));

    [Fact]
    public void Validate_allows_an_equals_form_output_flag_pointing_inside_the_repository() =>
        Assert.Null(_sanitizer.Validate("dotnet", ["build", "--output=out"], _root));

    [Theory]
    [InlineData("-p:OutputPath=../../escape")]
    [InlineData("-p:BaseOutputPath=../../escape")]
    [InlineData("-p:SomeCustomDir=../../escape")]
    [InlineData("-p:AnotherCustomPath=../../escape")]
    public void Validate_rejects_an_MSBuild_path_property_escaping_the_repository(string property) =>
        Assert.NotNull(_sanitizer.Validate("dotnet", ["build", property], _root));

    [Fact]
    public void Validate_allows_an_MSBuild_path_property_pointing_inside_the_repository() =>
        Assert.Null(_sanitizer.Validate("dotnet", ["build", "-p:OutputPath=out"], _root));

    [Fact]
    public void Validate_ignores_an_MSBuild_property_that_is_not_a_known_path_property() =>
        Assert.Null(_sanitizer.Validate("dotnet", ["build", "-p:Configuration=Release"], _root));

    [Fact]
    public void Validate_checks_every_semicolon_separated_MSBuild_property_in_one_token()
    {
        Assert.Null(_sanitizer.Validate("dotnet", ["build", "-p:Configuration=Release;OutputPath=out"], _root));
        Assert.NotNull(_sanitizer.Validate("dotnet", ["build", "-p:Configuration=Release;OutputPath=../../escape"], _root));
    }

    [Fact]
    public void Validate_rejects_a_git_output_flag_escaping_the_repository() =>
        Assert.NotNull(_sanitizer.Validate("git", ["log", "--output", Path.Combine("..", "..", "escape")], _root));

    [Fact]
    public void Validate_allows_a_git_output_flag_pointing_inside_the_repository() =>
        Assert.Null(_sanitizer.Validate("git", ["log", "--output=out"], _root));

    [Fact]
    public void Validate_ignores_an_unrecognized_flag_even_with_a_path_looking_value() =>
        // Documents the heuristic boundary deliberately - not a full CLI parser, so a flag outside
        // the curated known set is left alone (see IPathSanitizer's own remarks).
        Assert.Null(_sanitizer.Validate("dotnet", ["build", "--not-a-real-flag", Path.Combine("..", "..", "escape")], _root));
}
