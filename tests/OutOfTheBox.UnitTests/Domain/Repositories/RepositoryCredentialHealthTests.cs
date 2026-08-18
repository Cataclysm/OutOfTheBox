// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.UnitTests.Domain.Repositories;

public sealed class RepositoryCredentialHealthTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private const string RepositoryPath = @"C:\repos\example";

    [Fact]
    public void No_recorded_health_does_not_need_a_credential() =>
        Assert.False(RepositoryCredentialHealth.NeedsCredential(null));

    [Fact]
    public void A_recorded_failure_with_no_success_needs_a_credential() =>
        Assert.True(RepositoryCredentialHealth.NeedsCredential(new RepositoryCredentialHealth(RepositoryPath, Earlier, null)));

    [Fact]
    public void A_recorded_success_with_no_failure_does_not_need_a_credential() =>
        Assert.False(RepositoryCredentialHealth.NeedsCredential(new RepositoryCredentialHealth(RepositoryPath, null, Earlier)));

    [Fact]
    public void A_failure_more_recent_than_a_success_needs_a_credential() =>
        Assert.True(RepositoryCredentialHealth.NeedsCredential(new RepositoryCredentialHealth(RepositoryPath, Later, Earlier)));

    [Fact]
    public void A_success_more_recent_than_a_failure_does_not_need_a_credential() =>
        Assert.False(RepositoryCredentialHealth.NeedsCredential(new RepositoryCredentialHealth(RepositoryPath, Earlier, Later)));
}
