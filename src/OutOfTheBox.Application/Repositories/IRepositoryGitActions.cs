// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// Runs a quick, local-completion git sync action (pull/push/force-push/fetch/clean) against a named
/// repository, holding its per-repository lock for the duration - dashboard/background-sampler only,
/// per specs/repository-management's "Dashboard-only pull/push/force-push/fetch/clean actions"
/// requirement (no streamed output, no run-history record; the caller only needs to recolor an icon
/// or close a dialog on completion). Split out of the former single <c>IRepositoryManager</c> - see
/// its own remarks - since <c>RepositoryQuickActions.razor</c> and <c>RepositoryFetchSampler</c> only
/// ever need this slice, never the lifecycle/branch/history methods the other three interfaces cover.
/// </summary>
public interface IRepositoryGitActions
{
    /// <summary>Runs <c>git pull</c> against the named repository.</summary>
    Task<RepositoryGitActionResult> PullAsync(string name, CancellationToken cancellationToken);

    /// <summary>Runs <c>git push</c> against the named repository. See <see cref="PullAsync"/>'s own remarks.</summary>
    Task<RepositoryGitActionResult> PushAsync(string name, CancellationToken cancellationToken);

    /// <summary>Runs <c>git push --force</c> against the named repository. See <see cref="PullAsync"/>'s own remarks; the dashboard requires confirmation before calling this given its irreversibility.</summary>
    Task<RepositoryGitActionResult> ForcePushAsync(string name, CancellationToken cancellationToken);

    /// <summary>Runs <c>git fetch</c> against the named repository. See <see cref="PullAsync"/>'s own remarks.</summary>
    Task<RepositoryGitActionResult> FetchAsync(string name, CancellationToken cancellationToken);

    /// <summary>Runs <c>git clean -xdf</c> against the named repository. See <see cref="PullAsync"/>'s own remarks; the dashboard requires confirmation before calling this given its irreversibility.</summary>
    Task<RepositoryGitActionResult> CleanAsync(string name, CancellationToken cancellationToken);
}
