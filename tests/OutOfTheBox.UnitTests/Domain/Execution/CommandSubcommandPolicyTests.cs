// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Execution;

namespace OutOfTheBox.UnitTests.Domain.Execution;

public sealed class CommandSubcommandPolicyTests
{
    [Theory]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("BUILD")]
    [InlineData("publish")]
    [InlineData("pack")]
    [InlineData("run")]
    [InlineData("nuget")]
    [InlineData("clean")]
    [InlineData("workload")]
    public void IsKnownSubcommand_accepts_every_catalogued_dotnet_subcommand(string subcommand) =>
        Assert.True(CommandSubcommandPolicy.IsKnownSubcommand("dotnet", subcommand));

    [Theory]
    [InlineData("uninstall")]
    [InlineData("")]
    [InlineData("--not-a-subcommand")]
    public void IsKnownSubcommand_rejects_an_uncatalogued_dotnet_subcommand(string subcommand) =>
        Assert.False(CommandSubcommandPolicy.IsKnownSubcommand("dotnet", subcommand));

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
    [InlineData("push")]
    [InlineData("reset")]
    [InlineData("clean")]
    [InlineData("rebase")]
    public void IsKnownSubcommand_accepts_every_catalogued_git_subcommand(string subcommand) =>
        Assert.True(CommandSubcommandPolicy.IsKnownSubcommand("git", subcommand));

    [Theory]
    [InlineData("clone")]
    [InlineData("-C")]
    [InlineData("--git-dir=../elsewhere/.git")]
    public void IsKnownSubcommand_rejects_an_uncatalogued_git_subcommand_including_global_redirect_flags(string subcommand) =>
        Assert.False(CommandSubcommandPolicy.IsKnownSubcommand("git", subcommand));

    [Fact]
    public void IsKnownSubcommand_rejects_any_subcommand_for_an_unknown_executable() =>
        Assert.False(CommandSubcommandPolicy.IsKnownSubcommand("powershell", "build"));

    [Theory]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("test")]
    public void IsDefaultEnabled_is_true_for_the_originally_allowed_dotnet_subcommands(string subcommand) =>
        Assert.True(CommandSubcommandPolicy.IsDefaultEnabled("dotnet", subcommand));

    [Theory]
    [InlineData("publish")]
    [InlineData("pack")]
    [InlineData("clean")]
    [InlineData("dev-certs")]
    public void IsDefaultEnabled_is_false_for_a_newly_catalogued_dotnet_subcommand(string subcommand) =>
        Assert.False(CommandSubcommandPolicy.IsDefaultEnabled("dotnet", subcommand));

    [Theory]
    [InlineData("fetch")]
    [InlineData("checkout")]
    [InlineData("pull")]
    [InlineData("status")]
    public void IsDefaultEnabled_is_true_for_the_originally_allowed_git_subcommands(string subcommand) =>
        Assert.True(CommandSubcommandPolicy.IsDefaultEnabled("git", subcommand));

    [Theory]
    [InlineData("push")]
    [InlineData("reset")]
    [InlineData("rebase")]
    [InlineData("worktree")]
    public void IsDefaultEnabled_is_false_for_a_newly_catalogued_git_subcommand(string subcommand) =>
        Assert.False(CommandSubcommandPolicy.IsDefaultEnabled("git", subcommand));

    [Fact]
    public void IsDefaultEnabled_is_false_for_an_uncatalogued_subcommand() =>
        Assert.False(CommandSubcommandPolicy.IsDefaultEnabled("dotnet", "not-a-real-subcommand"));

    [Fact]
    public void KnownSubcommandsFor_reports_the_full_catalog_per_executable()
    {
        Assert.Equal(20, CommandSubcommandPolicy.KnownSubcommandsFor("dotnet").Count);
        Assert.Equal(29, CommandSubcommandPolicy.KnownSubcommandsFor("git").Count);
        Assert.Empty(CommandSubcommandPolicy.KnownSubcommandsFor("powershell"));
    }
}
