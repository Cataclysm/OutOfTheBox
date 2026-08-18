// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Repositories;

/// <summary>The outcome of an <see cref="IGitCredentialStore.RevokeAsync"/> call.</summary>
public abstract record GitCredentialRevokeResult
{
    /// <summary>The host's credential was removed from the git credential helper and this service's own record of it.</summary>
    public sealed record Revoked : GitCredentialRevokeResult;

    /// <summary>The host had no stored credential - distinguished from a genuine failure, per specs/mcp-git-credentials.</summary>
    public sealed record NothingToRevoke : GitCredentialRevokeResult;

    /// <summary><c>git.exe</c> could not be started at all.</summary>
    public sealed record GitUnreachable(string ErrorMessage) : GitCredentialRevokeResult;
}
