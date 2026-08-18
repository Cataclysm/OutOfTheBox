// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// Whether a remote host's credential currently appears to work - separate from
/// <see cref="GitHostAuthorization"/> ("what's configured") since a host can need attention whether
/// or not it was ever explicitly authorized through this service (e.g. a clone attempted cold
/// against a private repository with no prior setup). Updated only by operations with an
/// unambiguous single target host (pull/push/force-push/fetch/clone) - never by <c>git clean</c>
/// (no network) or arbitrary <c>git_run</c> commands (no reliable single-host attribution).
/// </summary>
public sealed record GitHostCredentialHealth(string Host, DateTimeOffset? LastAuthFailureAtUtc, DateTimeOffset? LastAuthSuccessAtUtc)
{
    /// <summary>
    /// True when the most recent recorded outcome for this host was an authentication failure -
    /// i.e. a failure timestamp exists and is not older than the success timestamp (or there is no
    /// recorded success at all). A later success always clears this, per specs/repository-management's
    /// "A successful operation clears a prior failure" scenario.
    /// </summary>
    public static bool NeedsCredential(GitHostCredentialHealth? health) =>
        health?.LastAuthFailureAtUtc is { } failure && (health.LastAuthSuccessAtUtc is not { } success || failure > success);
}
