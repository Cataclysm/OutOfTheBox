// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>
/// Maps <c>GET /version</c>, per design.md's "Dashboard branding" decision: exposes the same build
/// version the dashboard's header/footer displays, for upgrade verification and for any external
/// check that doesn't want to open the dashboard. Deliberately unauthenticated - a version/health
/// check needs to be reachable without first obtaining the shared credential.
/// </summary>
public static class VersionEndpoints
{
    /// <summary>Maps <c>GET /version</c>.</summary>
    public static IEndpointRouteBuilder MapVersionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/version", () => Results.Ok(new { name = VersionInfo.DisplayName, version = VersionInfo.Current }));

        return endpoints;
    }
}
