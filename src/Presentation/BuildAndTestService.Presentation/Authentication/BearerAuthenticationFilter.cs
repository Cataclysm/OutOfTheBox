using BuildAndTestService.Application.Configuration;
using BuildAndTestService.Domain.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BuildAndTestService.Presentation.Authentication;

/// <summary>
/// Minimal API endpoint filter requiring a valid <c>Authorization: Bearer &lt;token&gt;</c> header,
/// checked against <see cref="ServiceOptions.BearerToken"/> with a constant-time comparison.
/// A missing or invalid credential short-circuits with 401 before the endpoint handler runs.
/// Attach explicitly via <c>.AddEndpointFilter&lt;BearerAuthenticationFilter&gt;()</c> on the
/// command-execution and cancellation endpoints - it is not applied pipeline-wide, since the
/// dashboard uses a separate cookie-based login instead.
/// </summary>
public sealed class BearerAuthenticationFilter(IOptions<ServiceOptions> options) : IEndpointFilter
{
    private const string BearerPrefix = "Bearer ";

    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        var provided = header.StartsWith(BearerPrefix, StringComparison.Ordinal)
            ? header[BearerPrefix.Length..]
            : null;

        if (!CredentialComparer.Matches(provided, options.Value.BearerToken))
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        return next(context);
    }
}
