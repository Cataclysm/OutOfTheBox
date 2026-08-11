// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.UnitTests.Domain.Repositories;

public sealed class GitHostCredentialHealthTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_recorded_health_does_not_need_a_credential() =>
        Assert.False(GitHostCredentialHealth.NeedsCredential(null));

    [Fact]
    public void A_recorded_failure_with_no_success_needs_a_credential() =>
        Assert.True(GitHostCredentialHealth.NeedsCredential(new GitHostCredentialHealth("github.com", Earlier, null)));

    [Fact]
    public void A_recorded_success_with_no_failure_does_not_need_a_credential() =>
        Assert.False(GitHostCredentialHealth.NeedsCredential(new GitHostCredentialHealth("github.com", null, Earlier)));

    [Fact]
    public void A_failure_more_recent_than_a_success_needs_a_credential() =>
        Assert.True(GitHostCredentialHealth.NeedsCredential(new GitHostCredentialHealth("github.com", Later, Earlier)));

    [Fact]
    public void A_success_more_recent_than_a_failure_does_not_need_a_credential() =>
        Assert.False(GitHostCredentialHealth.NeedsCredential(new GitHostCredentialHealth("github.com", Earlier, Later)));
}
