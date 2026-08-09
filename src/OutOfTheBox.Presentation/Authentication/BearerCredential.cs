// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Authentication;

/// <summary>
/// Extracts the credential from an <c>Authorization: Bearer &lt;token&gt;</c> header value, shared
/// by <see cref="BearerAuthenticationFilter"/> (an <see cref="Microsoft.AspNetCore.Http.IEndpointFilter"/>,
/// usable only on route builders that support endpoint filters) and the MCP endpoint's own
/// authentication middleware (per <c>mcp-server</c>'s spec) - <c>MapMcp()</c> returns a plain
/// <see cref="Microsoft.AspNetCore.Builder.IEndpointConventionBuilder"/>, which does not support
/// <c>AddEndpointFilter</c>, so the MCP route needs middleware instead of a filter, but the same
/// header-parsing logic either way.
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
