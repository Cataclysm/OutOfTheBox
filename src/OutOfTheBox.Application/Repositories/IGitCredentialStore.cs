// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Stores, lists, and revokes PAT-based git credentials for a remote host via the configured git
/// credential helper (<c>git credential approve</c>/<c>fill</c>/<c>reject</c>), and tracks each
/// host's observed authentication health - reachable via the <c>authorize_git_host</c>/
/// <c>list_authorized_git_hosts</c>/<c>revoke_git_host_authorization</c> MCP tools and the
/// dashboard's PAT-prompt dialog/change-credential action, both calling this same port so the two
/// surfaces can never end up with divergent credentials for the same host. The token itself is
/// never persisted by this service, and is never returned via any MCP tool result - <see cref="GetCurrentTokenAsync"/>
/// is a dashboard-only exception (the credentials management page's "PAT fully shown" requirement),
/// fetching it live from the git credential helper on demand rather than storing it.
/// </summary>
public interface IGitCredentialStore
{
    /// <summary>
    /// Stores <paramref name="token"/> as the credential for <paramref name="host"/> via <c>git
    /// credential approve</c>, then verifies it is retrievable via <c>git credential fill</c> before
    /// reporting success. Replaces any existing credential for the same host (both use one fixed,
    /// internal placeholder username - see design.md's "no username parameter" decision - so the
    /// (protocol, host) pair alone is always the match key).
    /// </summary>
    Task<GitCredentialAuthorizeResult> AuthorizeAsync(string host, string token, CancellationToken cancellationToken);

    /// <summary>Every host previously authorized via <see cref="AuthorizeAsync"/> that has not since been revoked, with its authorization timestamp and current health.</summary>
    Task<IReadOnlyList<GitHostAuthorizationSummary>> ListAuthorizedHostsAsync(CancellationToken cancellationToken);

    /// <summary>Removes <paramref name="host"/>'s credential from the git credential helper and this service's own record of it.</summary>
    Task<GitCredentialRevokeResult> RevokeAsync(string host, CancellationToken cancellationToken);

    /// <summary>
    /// Records whether a network-touching git operation (pull/push/force-push/fetch/clone) against
    /// <paramref name="host"/> succeeded or failed for an authentication reason - the signal
    /// <see cref="GitHostCredentialHealth.NeedsCredential"/> is derived from, feeding
    /// <c>list_authorized_git_hosts</c>'s health field. Called regardless of whether the host was
    /// ever explicitly authorized, per specs/repository-management's "A failure marks the host even
    /// without a prior explicit authorization" scenario. Does not affect any individual repository's
    /// own needs-credential marker - see <see cref="RecordRepositoryOutcomeAsync"/> for that.
    /// </summary>
    Task RecordOutcomeAsync(string host, bool succeeded, CancellationToken cancellationToken);

    /// <summary><paramref name="host"/>'s currently recorded health, or <see langword="null"/> if nothing has ever been recorded for it.</summary>
    Task<GitHostCredentialHealth?> GetHealthAsync(string host, CancellationToken cancellationToken);

    /// <summary>
    /// Records whether a network-touching git operation (pull/push/force-push/fetch/clone) against
    /// the repository at <paramref name="repositoryPath"/> succeeded or failed for an authentication
    /// reason - the signal <see cref="RepositoryCredentialHealth.NeedsCredential"/> is derived from,
    /// feeding the dashboard/<c>list_repositories</c>' per-repository needs-credential marker.
    /// Scoped to this one repository, not its remote host, so a broken (or fixed) credential on one
    /// repository never marks or clears a sibling repository that shares the same host but was never
    /// itself touched - see <see cref="RepositoryCredentialHealth"/>'s own remarks.
    /// </summary>
    Task RecordRepositoryOutcomeAsync(string repositoryPath, bool succeeded, CancellationToken cancellationToken);

    /// <summary><paramref name="repositoryPath"/>'s currently recorded credential health, or <see langword="null"/> if nothing has ever been recorded for it.</summary>
    Task<RepositoryCredentialHealth?> GetRepositoryHealthAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>
    /// Repoints a repository's recorded credential health from <paramref name="oldRepositoryPath"/>
    /// to <paramref name="newRepositoryPath"/> after a rename, the same way run history is repointed
    /// (<c>IRunRepository.UpdateRepositoryPathAsync</c>) - without this, renaming a repository that
    /// currently needs a credential would silently lose that mark instead of carrying it over. A
    /// no-op if nothing was recorded for the old path.
    /// </summary>
    Task RenameRepositoryHealthAsync(string oldRepositoryPath, string newRepositoryPath, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches <paramref name="host"/>'s current credential straight from the git credential helper
    /// (<c>git credential fill</c>), for the dashboard's credentials management page - never called
    /// from any MCP tool. Returns <see langword="null"/> if no credential is retrievable (none
    /// stored, the helper isn't configured, or git.exe is unreachable) rather than throwing, so a
    /// page listing several credentials can show one row as unavailable without failing the rest.
    /// </summary>
    Task<string?> GetCurrentTokenAsync(string host, CancellationToken cancellationToken);
}
