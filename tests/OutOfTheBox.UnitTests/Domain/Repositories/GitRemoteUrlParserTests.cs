// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.UnitTests.Domain.Repositories;

public sealed class GitRemoteUrlParserTests
{
    [Theory]
    [InlineData("https://github.com/example/repo.git", "github.com")]
    [InlineData("http://dev.azure.com/org/project/_git/repo", "dev.azure.com")]
    [InlineData("https://user@github.com/example/repo.git", "github.com")]
    [InlineData("https://github.com:443/example/repo.git", "github.com")]
    [InlineData("ssh://git@github.com/example/repo.git", "github.com")]
    public void Absolute_URLs_resolve_to_their_host(string url, string expectedHost)
    {
        Assert.True(GitRemoteUrlParser.TryGetHost(url, out var host));
        Assert.Equal(expectedHost, host);
    }

    [Theory]
    [InlineData("git@github.com:example/repo.git", "github.com")]
    [InlineData("git@dev.azure.com:v3/org/project/repo", "dev.azure.com")]
    public void SCP_like_URLs_resolve_to_their_host(string url, string expectedHost)
    {
        Assert.True(GitRemoteUrlParser.TryGetHost(url, out var host));
        Assert.Equal(expectedHost, host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url at all")]
    [InlineData("C:\\repos\\local-repo")]
    public void Malformed_or_local_input_does_not_resolve(string? url) =>
        Assert.False(GitRemoteUrlParser.TryGetHost(url, out _));
}
