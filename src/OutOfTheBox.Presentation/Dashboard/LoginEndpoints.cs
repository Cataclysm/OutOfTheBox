// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.Security.Claims;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Domain.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>
/// Maps the dashboard's login/logout endpoints, per design.md's "Dashboard auth" decision: the
/// operator enters the same shared token the API uses, exchanged for a standard ASP.NET Core auth
/// cookie. Plain minimal-API POST handlers, not Blazor component methods - a Blazor Server circuit
/// can't set a response cookie mid-circuit, so the login page's <c>&lt;form&gt;</c> submits here as
/// a genuine full-page HTTP POST instead of a SignalR round-trip. Deliberately mapped under
/// <c>/account/...</c> rather than <c>/login</c>/<c>/logout</c> directly - <c>/login</c> is also a
/// routable Blazor page (<see cref="Login"/>), and <c>MapRazorComponents</c> registers a page route
/// against every HTTP method (to support Blazor's own enhanced-navigation form posts), so a plain
/// <c>MapPost("/login", ...)</c> at the same path throws <c>AmbiguousMatchException</c> at request
/// time - caught by actually running the login flow, not by review or the test suite.
/// </summary>
public static class LoginEndpoints
{
    /// <summary>Maps <c>POST /account/login</c> and <c>POST /account/logout</c>.</summary>
    public static IEndpointRouteBuilder MapLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/login", HandleLoginAsync);
        endpoints.MapPost("/account/logout", HandleLogoutAsync);

        return endpoints;
    }

    // Returns Task (via httpContext.Response.Redirect), not Task<IResult> (via Results.Redirect) -
    // caught by actually running the login flow: a lambda/method whose parameter list is exactly
    // (HttpContext) binds to minimal API's older RequestDelegate-shaped overload, which treats a
    // Task<IResult> as a plain Task and silently discards the IResult, so the redirect never
    // happened (200 with no Location header instead of the intended 302) - reproduced only for the
    // single-HttpContext-parameter shape (HandleLogoutAsync originally), not the two-parameter one
    // (HandleLoginAsync, which happened to work by accident of its different signature). Matches
    // the ASP0016 build warning both handlers had - not a false positive after all.
    private static async Task HandleLoginAsync(HttpContext httpContext, IOptions<ServiceOptions> options)
    {
        var form = await httpContext.Request.ReadFormAsync();
        var token = form["token"].ToString();

        if (!CredentialComparer.Matches(token, options.Value.BearerToken))
        {
            httpContext.Response.Redirect("/login?error=1");
            return;
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "operator")],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        httpContext.Response.Redirect("/");
    }

    private static async Task HandleLogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        httpContext.Response.Redirect("/login");
    }
}
