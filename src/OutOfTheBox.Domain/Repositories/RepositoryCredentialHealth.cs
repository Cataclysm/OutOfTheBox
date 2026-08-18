// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// Whether a specific repository's most recent network-touching git operation (pull/push/
/// force-push/fetch/clone) against it looked like an authentication failure - scoped to the
/// repository itself, not its remote host. <see cref="GitHostCredentialHealth"/> (host-scoped) is
/// deliberately kept separate and still used for <c>list_authorized_git_hosts</c>'s "is the
/// credential I configured for this host working" question; this type answers the dashboard/
/// <c>list_repositories</c>' different question, "does this specific repository need attention" -
/// a host with both a private and a public repository (or several private ones with different
/// actual state) must not have one repository's broken credential mark, or a fix on one repository
/// clear, a sibling that was never itself touched (confirmed live: exactly this cross-contamination,
/// reported as a real bug against the earlier host-scoped-only tracking).
/// </summary>
public sealed record RepositoryCredentialHealth(string RepositoryPath, DateTimeOffset? LastAuthFailureAtUtc, DateTimeOffset? LastAuthSuccessAtUtc)
{
    /// <summary>
    /// True when the most recent recorded outcome for this repository was an authentication failure -
    /// i.e. a failure timestamp exists and is not older than the success timestamp (or there is no
    /// recorded success at all). A later success always clears this, per specs/repository-management's
    /// "A successful operation clears a prior failure" scenario.
    /// </summary>
    public static bool NeedsCredential(RepositoryCredentialHealth? health) =>
        health?.LastAuthFailureAtUtc is { } failure && (health.LastAuthSuccessAtUtc is not { } success || failure > success);
}
