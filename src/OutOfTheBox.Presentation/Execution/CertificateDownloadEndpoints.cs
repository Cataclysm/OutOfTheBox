// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Presentation.Execution;

/// <summary>
/// Maps <c>GET /dashboard-certificate</c> - lets an authenticated operator download the service's
/// public-only, PEM-encoded HTTPS certificate (see <see cref="ServiceOptions.CertificateFilePath"/>)
/// from the About page, so they can trust it on a Windows PC and hand it to the sbx sandbox, without
/// needing to extract it from the password-protected PFX Kestrel itself binds to. Gated by the
/// dashboard's own cookie authentication (<c>RequireAuthorization()</c>), the same reasoning as
/// <see cref="RepositoryFileDownloadEndpoints"/> - a plain browser navigation, not a bearer-token call.
/// </summary>
public static class CertificateDownloadEndpoints
{
    /// <summary>Maps <c>GET /dashboard-certificate</c>, requiring the dashboard's own (cookie) authentication.</summary>
    public static IEndpointRouteBuilder MapCertificateDownloadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/dashboard-certificate", HandleDownload)
            .RequireAuthorization();

        return endpoints;
    }

    private static IResult HandleDownload(IOptions<ServiceOptions> options)
    {
        var path = options.Value.CertificateFilePath;
        return string.IsNullOrEmpty(path) || !File.Exists(path)
            ? Results.NotFound()
            : Results.File(path, "application/x-x509-ca-cert", "outofthebox.cer");
    }
}
