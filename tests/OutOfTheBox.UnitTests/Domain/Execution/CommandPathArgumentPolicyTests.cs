// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Execution;

namespace OutOfTheBox.UnitTests.Domain.Execution;

/// <summary>
/// Exercises <see cref="CommandPathArgumentPolicy"/> directly - pure string extraction, no
/// <c>IWorkingDirectoryResolver</c>/filesystem involved at all, unlike <c>PathSanitizerTests</c> (which
/// still exercises the full <c>Validate</c> pipeline against a real resolver).
/// </summary>
public sealed class CommandPathArgumentPolicyTests
{
    [Theory]
    [InlineData("dotnet", new[] { "build" })]
    [InlineData("dotnet", new[] { "test", "--filter", "MyTests" })]
    [InlineData("git", new[] { "status" })]
    public void ExtractCandidatePaths_finds_nothing_for_arguments_with_no_path_bearing_flags(string executable, string[] arguments) =>
        Assert.Empty(CommandPathArgumentPolicy.ExtractCandidatePaths(executable, arguments));

    [Theory]
    [InlineData("-o")]
    [InlineData("--output")]
    [InlineData("--results-directory")]
    public void ExtractCandidatePaths_finds_a_dotnet_output_flags_next_argument_as_its_value(string flag)
    {
        var candidate = Assert.Single(CommandPathArgumentPolicy.ExtractCandidatePaths("dotnet", ["test", flag, "out"]));

        Assert.Equal(flag, candidate.Label);
        Assert.Equal("out", candidate.Value);
    }

    [Fact]
    public void ExtractCandidatePaths_finds_an_equals_form_output_flags_value()
    {
        var candidate = Assert.Single(CommandPathArgumentPolicy.ExtractCandidatePaths("dotnet", ["build", "--output=out"]));

        Assert.Equal("--output", candidate.Label);
        Assert.Equal("out", candidate.Value);
    }

    [Fact]
    public void ExtractCandidatePaths_finds_nothing_for_a_bare_trailing_flag_with_no_value() =>
        Assert.Empty(CommandPathArgumentPolicy.ExtractCandidatePaths("dotnet", ["build", "--output"]));

    [Theory]
    [InlineData("-p:OutputPath=out")]
    [InlineData("-p:BaseOutputPath=out")]
    [InlineData("-p:SomeCustomDir=out")]
    [InlineData("-p:AnotherCustomPath=out")]
    public void ExtractCandidatePaths_finds_a_known_MSBuild_path_property(string property)
    {
        var candidate = Assert.Single(CommandPathArgumentPolicy.ExtractCandidatePaths("dotnet", ["build", property]));

        Assert.Equal("out", candidate.Value);
    }

    [Fact]
    public void ExtractCandidatePaths_ignores_an_MSBuild_property_that_is_not_a_known_path_property() =>
        Assert.Empty(CommandPathArgumentPolicy.ExtractCandidatePaths("dotnet", ["build", "-p:Configuration=Release"]));

    [Fact]
    public void ExtractCandidatePaths_finds_every_semicolon_separated_MSBuild_property_in_one_token()
    {
        var candidates = CommandPathArgumentPolicy.ExtractCandidatePaths("dotnet", ["build", "-p:Configuration=Release;OutputPath=out;BaseOutputPath=out2"]).ToList();

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.Label == "-p:OutputPath" && c.Value == "out");
        Assert.Contains(candidates, c => c.Label == "-p:BaseOutputPath" && c.Value == "out2");
    }

    [Fact]
    public void ExtractCandidatePaths_finds_a_git_output_flags_value() =>
        Assert.Single(CommandPathArgumentPolicy.ExtractCandidatePaths("git", ["log", "--output=out"]));

    [Fact]
    public void ExtractCandidatePaths_ignores_dotnet_only_MSBuild_properties_for_git() =>
        Assert.Empty(CommandPathArgumentPolicy.ExtractCandidatePaths("git", ["log", "-p:OutputPath=out"]));

    [Fact]
    public void ExtractCandidatePaths_ignores_an_unrecognized_flag_even_with_a_path_looking_value() =>
        // Documents the heuristic boundary deliberately - not a full CLI parser, so a flag outside
        // the curated known set is left alone (see IPathSanitizer's own remarks).
        Assert.Empty(CommandPathArgumentPolicy.ExtractCandidatePaths("dotnet", ["build", "--not-a-real-flag", "out"]));
}
