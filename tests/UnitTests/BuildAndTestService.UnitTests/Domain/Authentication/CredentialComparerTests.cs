// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using BuildAndTestService.Domain.Authentication;

namespace BuildAndTestService.UnitTests.Domain.Authentication;

public sealed class CredentialComparerTests
{
    [Fact]
    public void Matches_returns_true_for_identical_credentials()
    {
        Assert.True(CredentialComparer.Matches("correct-token", "correct-token"));
    }

    [Fact]
    public void Matches_returns_false_for_different_credentials()
    {
        Assert.False(CredentialComparer.Matches("wrong-token", "correct-token"));
    }

    [Fact]
    public void Matches_returns_false_for_different_length_credentials()
    {
        Assert.False(CredentialComparer.Matches("short", "a-much-longer-correct-token"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Matches_returns_false_for_missing_provided_credential(string? provided)
    {
        Assert.False(CredentialComparer.Matches(provided, "correct-token"));
    }

    [Fact]
    public void Matches_returns_false_when_expected_credential_is_empty()
    {
        // An unconfigured/empty expected credential must never accidentally accept anything.
        Assert.False(CredentialComparer.Matches("anything", string.Empty));
    }

    [Fact]
    public void Matches_is_case_sensitive()
    {
        Assert.False(CredentialComparer.Matches("Correct-Token", "correct-token"));
    }
}
