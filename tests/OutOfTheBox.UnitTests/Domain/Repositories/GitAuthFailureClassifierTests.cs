// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.UnitTests.Domain.Repositories;

public sealed class GitAuthFailureClassifierTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_null_stderr_is_not_an_auth_failure(string? stderr) =>
        Assert.False(GitAuthFailureClassifier.IsLikelyAuthFailure(stderr));

    [Theory]
    [InlineData("fatal: Authentication failed for 'https://github.com/example/repo.git'")]
    [InlineData("remote: Invalid username or password.")]
    [InlineData("fatal: could not read Username for 'https://dev.azure.com': terminal prompts disabled")]
    [InlineData("fatal: could not read Password for 'https://github.com': terminal prompts disabled")]
    [InlineData("remote: Bad credentials")]
    [InlineData("remote: HTTP Basic: Access denied")]
    [InlineData("fatal: unable to access '...': The requested URL returned error: 401")]
    [InlineData("fatal: unable to access '...': The requested URL returned error: 403")]
    public void Known_auth_failure_phrasings_are_classified_as_auth_failures(string stderr) =>
        Assert.True(GitAuthFailureClassifier.IsLikelyAuthFailure(stderr));

    [Theory]
    [InlineData("fatal: repository 'https://example.com/missing.git' not found")]
    [InlineData("fatal: unable to access '...': Could not resolve host: example.com")]
    [InlineData("error: failed to push some refs")]
    [InlineData("fatal: destination path 'repo' already exists and is not an empty directory.")]
    public void Unrelated_failures_are_not_classified_as_auth_failures(string stderr) =>
        Assert.False(GitAuthFailureClassifier.IsLikelyAuthFailure(stderr));

    [Fact]
    public void Classification_is_case_insensitive() =>
        Assert.True(GitAuthFailureClassifier.IsLikelyAuthFailure("FATAL: AUTHENTICATION FAILED FOR 'https://github.com'"));
}
