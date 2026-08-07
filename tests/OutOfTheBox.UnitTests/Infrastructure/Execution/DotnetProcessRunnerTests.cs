// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Execution;
using OutOfTheBox.Infrastructure.Execution;

namespace OutOfTheBox.UnitTests.Infrastructure.Execution;

public sealed class DotnetProcessRunnerTests
{
    [Fact]
    public void BuildStartInfo_never_uses_shell_execute()
    {
        var request = new ProcessRunRequest(["build"], @"C:\repos\myrepo");

        var startInfo = DotnetProcessRunner.BuildStartInfo(request);

        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void BuildStartInfo_passes_an_argument_containing_shell_metacharacters_as_one_literal_entry()
    {
        // Mirrors specs/dotnet-command-execution's scenario: a single array element containing
        // something like "; rm -rf /" must reach dotnet.exe as one literal argv entry, never
        // concatenated into a string a shell could re-parse.
        const string maliciousLookingArgument = "; rm -rf / & echo INJECTED > pwned.txt";
        var request = new ProcessRunRequest(["test", "--filter", maliciousLookingArgument], @"C:\repos\myrepo");

        var startInfo = DotnetProcessRunner.BuildStartInfo(request);

        Assert.Equal(3, startInfo.ArgumentList.Count);
        Assert.Equal("test", startInfo.ArgumentList[0]);
        Assert.Equal("--filter", startInfo.ArgumentList[1]);
        Assert.Equal(maliciousLookingArgument, startInfo.ArgumentList[2]);
    }

    [Fact]
    public void BuildStartInfo_sets_the_working_directory_and_dotnet_as_the_file_name()
    {
        var request = new ProcessRunRequest(["--version"], @"C:\repos\myrepo\src");

        var startInfo = DotnetProcessRunner.BuildStartInfo(request);

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal(@"C:\repos\myrepo\src", startInfo.WorkingDirectory);
    }

    [Fact]
    public void BuildStartInfo_redirects_both_output_streams()
    {
        var request = new ProcessRunRequest(["build"], @"C:\repos\myrepo");

        var startInfo = DotnetProcessRunner.BuildStartInfo(request);

        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }
}
