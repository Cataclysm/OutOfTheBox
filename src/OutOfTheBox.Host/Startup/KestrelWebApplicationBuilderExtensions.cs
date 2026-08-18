// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Security.Authentication;

namespace OutOfTheBox.Host.Startup;

/// <summary>Enforces this service's HTTPS-only transport requirement (Section 16).</summary>
public static class KestrelWebApplicationBuilderExtensions
{
    /// <summary>
    /// Fails fast at startup if any configured Kestrel endpoint is plain HTTP, and restricts every
    /// HTTPS endpoint to TLS 1.2/1.3.
    /// </summary>
    public static void RequireHttpsKestrelEndpoints(this WebApplicationBuilder builder)
    {
        // The bearer token, command arguments/output, and the dashboard's cookie session all cross
        // this port, so plain HTTP would leak them to anyone on-path. Fails fast rather than silently
        // accepting an endpoint someone configured as "http://" by mistake - there is no legitimate
        // reason for this service to ever accept an unencrypted connection.
        foreach (var endpoint in builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            var url = endpoint["Url"];
            if (url is not null && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Kestrel endpoint '{endpoint.Key}' is configured as '{url}' - this service must not accept plain HTTP connections.");
            }
        }

        builder.WebHost.ConfigureKestrel(options =>
            options.ConfigureHttpsDefaults(https => https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13));
    }
}
