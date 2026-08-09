// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Domain.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Presentation.Authentication;

/// <summary>
/// Requires the shared bearer credential (per <c>service-authentication</c>) on the MCP endpoint -
/// as middleware, not an endpoint filter, since <c>MapMcp()</c> returns a plain
/// <see cref="Microsoft.AspNetCore.Builder.IEndpointConventionBuilder"/> that doesn't support
/// <c>AddEndpointFilter</c> the way a minimal-API route builder does. Per
/// <c>mcp-server</c>'s "Every MCP request requires the same bearer credential" requirement: a
/// missing or invalid credential is rejected with 401 before the MCP handshake, tool listing, or any
/// tool call is processed.
/// </summary>
public static class McpAuthenticationMiddleware
{
    /// <summary>Registers the bearer-credential check for requests under <paramref name="pathPrefix"/> (the MCP endpoint's own route, mapped separately via <c>MapMcp</c>).</summary>
    public static IApplicationBuilder UseMcpBearerAuthentication(this IApplicationBuilder app, string pathPrefix) =>
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(pathPrefix),
            branch => branch.Use(async (context, next) =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<ServiceOptions>>();
                var provided = BearerCredential.FromHeader(context.Request.Headers.Authorization.ToString());

                if (!CredentialComparer.Matches(provided, options.Value.BearerToken))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await next(context);
            }));
}
