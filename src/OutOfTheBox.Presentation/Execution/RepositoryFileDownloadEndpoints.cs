// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace OutOfTheBox.Presentation.Execution;

/// <summary>
/// Maps <c>GET /dashboard-files/{name}</c> - the file tree browser's download action, per
/// specs/repository-management's "Repository detail provides a file tree browser" requirement.
/// Deliberately gated by the dashboard's own cookie authentication (<c>RequireAuthorization()</c>,
/// the same scheme <c>MapRazorComponents&lt;App&gt;()</c> uses) rather than the bearer-token scheme
/// the MCP endpoint uses - this is a plain browser navigation (an <c>&lt;a href&gt;</c> the
/// operator clicks), which can carry a session cookie but can't attach a bearer `Authorization`
/// header. The route lives under its own <c>/dashboard-files/</c> prefix, not nested under
/// <c>/repository-management/</c>, so it can never collide with the dashboard's own Razor page route
/// for the same repository.
/// </summary>
public static class RepositoryFileDownloadEndpoints
{
    /// <summary>Maps <c>GET /dashboard-files/{name}</c>, requiring the dashboard's own (cookie) authentication.</summary>
    public static IEndpointRouteBuilder MapRepositoryFileDownloadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/dashboard-files/{name}", HandleDownloadAsync)
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> HandleDownloadAsync(
        string name, string? path, IRepositoryFileBrowser fileBrowser, CancellationToken cancellationToken)
    {
        var resolvedPath = await fileBrowser.ResolveConfinedFilePathAsync(name, path ?? string.Empty, cancellationToken);
        return resolvedPath is null
            ? Results.NotFound()
            : Results.File(resolvedPath, "application/octet-stream", Path.GetFileName(resolvedPath));
    }
}
