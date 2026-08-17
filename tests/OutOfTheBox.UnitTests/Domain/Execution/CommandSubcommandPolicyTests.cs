// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Execution;

namespace OutOfTheBox.UnitTests.Domain.Execution;

public sealed class CommandSubcommandPolicyTests
{
    [Theory]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("BUILD")]
    public void IsAllowed_accepts_the_allowed_dotnet_subcommands(string subcommand) =>
        Assert.True(CommandSubcommandPolicy.IsAllowed("dotnet", subcommand));

    [Theory]
    [InlineData("publish")]
    [InlineData("pack")]
    [InlineData("run")]
    [InlineData("nuget")]
    [InlineData("clean")]
    [InlineData("workload")]
    public void IsAllowed_rejects_other_dotnet_subcommands(string subcommand) =>
        Assert.False(CommandSubcommandPolicy.IsAllowed("dotnet", subcommand));

    [Theory]
    [InlineData("fetch")]
    [InlineData("checkout")]
    [InlineData("pull")]
    [InlineData("status")]
    [InlineData("log")]
    [InlineData("diff")]
    [InlineData("show")]
    [InlineData("branch")]
    [InlineData("rev-parse")]
    [InlineData("PULL")]
    public void IsAllowed_accepts_the_allowed_git_subcommands(string subcommand) =>
        Assert.True(CommandSubcommandPolicy.IsAllowed("git", subcommand));

    [Theory]
    [InlineData("push")]
    [InlineData("reset")]
    [InlineData("clean")]
    [InlineData("clone")]
    [InlineData("rebase")]
    [InlineData("-C")]
    [InlineData("--git-dir=../elsewhere/.git")]
    public void IsAllowed_rejects_other_git_subcommands_including_global_redirect_flags(string subcommand) =>
        Assert.False(CommandSubcommandPolicy.IsAllowed("git", subcommand));

    [Fact]
    public void IsAllowed_rejects_any_subcommand_for_an_unknown_executable() =>
        Assert.False(CommandSubcommandPolicy.IsAllowed("powershell", "build"));

    [Fact]
    public void AllowedSubcommandsFor_reports_the_full_set_per_executable()
    {
        Assert.Equal(3, CommandSubcommandPolicy.AllowedSubcommandsFor("dotnet").Count);
        Assert.Equal(9, CommandSubcommandPolicy.AllowedSubcommandsFor("git").Count);
        Assert.Empty(CommandSubcommandPolicy.AllowedSubcommandsFor("powershell"));
    }
}
