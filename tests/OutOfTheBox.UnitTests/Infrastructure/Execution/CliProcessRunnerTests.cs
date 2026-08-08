// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Execution;
using OutOfTheBox.Infrastructure.Execution;

namespace OutOfTheBox.UnitTests.Infrastructure.Execution;

public sealed class CliProcessRunnerTests
{
    [Theory]
    [InlineData("dotnet")]
    [InlineData("git")]
    public void BuildStartInfo_never_uses_shell_execute(string executable)
    {
        var request = new ProcessRunRequest(["build"], @"C:\repositories\myrepo", executable);

        var startInfo = CliProcessRunner.BuildStartInfo(request);

        Assert.False(startInfo.UseShellExecute);
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("git")]
    public void BuildStartInfo_passes_an_argument_containing_shell_metacharacters_as_one_literal_entry(string executable)
    {
        // Mirrors specs/dotnet-command-execution's and specs/git-command-execution's scenario: a
        // single array element containing something like "; rm -rf /" must reach the process as
        // one literal argv entry, never concatenated into a string a shell could re-parse.
        const string maliciousLookingArgument = "; rm -rf / & echo INJECTED > pwned.txt";
        var request = new ProcessRunRequest(["test", "--filter", maliciousLookingArgument], @"C:\repositories\myrepo", executable);

        var startInfo = CliProcessRunner.BuildStartInfo(request);

        Assert.Equal(3, startInfo.ArgumentList.Count);
        Assert.Equal("test", startInfo.ArgumentList[0]);
        Assert.Equal("--filter", startInfo.ArgumentList[1]);
        Assert.Equal(maliciousLookingArgument, startInfo.ArgumentList[2]);
    }

    [Fact]
    public void BuildStartInfo_sets_the_working_directory_and_dotnet_as_the_file_name()
    {
        var request = new ProcessRunRequest(["--version"], @"C:\repositories\myrepo\src", "dotnet");

        var startInfo = CliProcessRunner.BuildStartInfo(request);

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal(@"C:\repositories\myrepo\src", startInfo.WorkingDirectory);
    }

    [Fact]
    public void BuildStartInfo_sets_git_as_the_file_name_when_requested()
    {
        // The executable is always fixed by the calling endpoint, never read from caller
        // arguments - this confirms the runner honors whatever Executable it's given rather than
        // hardcoding "dotnet" (a regression here would silently break POST /run/git).
        var request = new ProcessRunRequest(["status"], @"C:\repositories\myrepo", "git");

        var startInfo = CliProcessRunner.BuildStartInfo(request);

        Assert.Equal("git", startInfo.FileName);
    }

    [Fact]
    public void BuildStartInfo_redirects_both_output_streams()
    {
        var request = new ProcessRunRequest(["build"], @"C:\repositories\myrepo", "dotnet");

        var startInfo = CliProcessRunner.BuildStartInfo(request);

        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }
}
