// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Presentation.Authentication;
using OutOfTheBox.Presentation.Dashboard;
using OutOfTheBox.Presentation.Execution;

namespace OutOfTheBox.Host.Startup;

/// <summary>Wires the HTTP request pipeline (middleware) and route/endpoint mappings.</summary>
public static class PipelineWebApplicationExtensions
{
    /// <summary>Adds static assets and the authentication/authorization/antiforgery middleware, in the order they must run.</summary>
    public static void UseOutOfTheBoxPipeline(this WebApplication app)
    {
        // Serves static content (dashboard.css, the vendored Chart.js/interop script, and the
        // framework-provided blazor.web.js, all referenced via @Assets[...] in App.razor rather than
        // hardcoded paths - required for MapStaticAssets to resolve them at all, not just for
        // fingerprinted caching) before authentication - none of it needs a login, and the dashboard's
        // own login page itself needs its stylesheet before the operator has a session. Without this,
        // every static asset 404s even though the files are physically present in the build/publish
        // output - there is no other middleware in this pipeline that serves them.
        app.MapStaticAssets();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
    }

    /// <summary>Maps every endpoint this service exposes - download links, login, MCP, and the dashboard itself.</summary>
    public static void MapOutOfTheBoxEndpoints(this WebApplication app)
    {
        app.MapRepositoryFileDownloadEndpoints();
        app.MapCertificateDownloadEndpoints();
        app.MapLoginEndpoints();
        app.MapVersionEndpoint();

        // MCP server (sbx-mcp-server) - the same shared bearer token service-authentication has always
        // used, applied to the MCP route via middleware (MapMcp's builder type doesn't support
        // AddEndpointFilter - see McpAuthenticationMiddleware's own remarks) so an unauthenticated
        // request is rejected before the MCP handshake, tool listing, or any tool call is processed,
        // per mcp-server's own requirement.
        app.UseMcpBearerAuthentication("/mcp");
        app.MapMcp("/mcp");

        // RequireAuthorization() applies to this Razor Components route group the same way it's
        // applied directly to MapRepositoryFileDownloadEndpoints/MapCertificateDownloadEndpoints above
        // (both cookie-authenticated download links, since they're plain browser navigations). Neither
        // the MCP route above (its own bearer-token middleware, not ASP.NET Core's cookie-based
        // authorization) nor the dashboard's own Login page (its [AllowAnonymous] attribute) are
        // affected by this.
        //
        // AddAdditionalAssemblies is required since App lives in Host rather than Presentation (moved
        // so its @Assets[...] references resolve against the actual hosting app's manifest, per
        // Section 15's Chart.js work) - MapRazorComponents<App>() only scans App's own assembly for
        // @page components by default, and every routable page (Status, Repositories, History, Login,
        // ...) still lives in Presentation.
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .AddAdditionalAssemblies(typeof(Status).Assembly)
            .RequireAuthorization();
    }
}
