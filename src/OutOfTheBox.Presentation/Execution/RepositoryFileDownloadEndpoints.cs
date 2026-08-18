// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;

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
    // The same built-in extension-to-MIME-type map ASP.NET Core's own static file middleware uses -
    // gives an accurate Content-Type (image/x-icon, application/pdf, audio/*, video/*, ...) instead
    // of the previous hardcoded application/octet-stream, which FilePreviewDialog's <img>/<iframe>/
    // <audio>/<video> previews rely on for correct rendering (most browsers tolerate a wrong
    // Content-Type for <img> via content sniffing, but an <iframe> PDF viewer or an <audio>/<video>
    // element generally does not). Content-Disposition still says "attachment" regardless (see
    // HandleDownloadAsync) - browsers apply that only to a top-level navigation/explicit download,
    // never to an embedded resource fetch like these, so the file tree's own plain "download" button
    // is unaffected by this change.
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

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
        if (resolvedPath is null)
        {
            return Results.NotFound();
        }

        var fileName = Path.GetFileName(resolvedPath);
        if (!ContentTypeProvider.TryGetContentType(fileName, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return Results.File(resolvedPath, contentType, fileName);
    }
}
