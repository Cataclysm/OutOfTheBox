// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Presentation.Authentication;

/// <summary>
/// Extracts the credential from an <c>Authorization: Bearer &lt;token&gt;</c> header value - used by
/// <see cref="McpAuthenticationMiddleware"/> to gate the MCP endpoint (per <c>mcp-server</c>'s spec),
/// pulled out into its own small class rather than inlined there so the header-parsing logic has
/// exactly one place to live regardless of which authentication mechanism ends up needing it.
/// </summary>
internal static class BearerCredential
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>Returns the token following <c>Bearer </c> in <paramref name="authorizationHeader"/>, or <see langword="null"/> if the header is missing or doesn't use that scheme.</summary>
    public static string? FromHeader(string authorizationHeader) =>
        authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal)
            ? authorizationHeader[BearerPrefix.Length..]
            : null;
}
