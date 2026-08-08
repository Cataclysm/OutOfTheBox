// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Presentation.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace OutOfTheBox.Presentation.Execution;

/// <summary>
/// Maps <c>GET /repositories</c> and <c>POST /repositories/clone</c>, per
/// specs/repository-management - the sbx caller can list and clone repositories, but never delete
/// one (that stays dashboard-only, no REST surface, per direct instruction: a remote caller with
/// delete access is a real risk this service's design has always deliberately avoided). Both reuse
/// <see cref="IRepositoryManager"/> directly - the exact same list/clone logic the dashboard's
/// <c>Repositories.razor</c> already calls in-process, so this is a REST facade over existing behavior,
/// not a second implementation of it. Deliberately spelled out as <c>/repositories</c>, not
/// <c>/repositories</c>: the dashboard already owns the bare <c>/repositories</c>/<c>/repositories/{Name}</c> Blazor
/// page routes, and <c>MapRazorComponents</c> registers a page route against every HTTP method (a
/// real collision found on first attempt - the exact same class of bug tasks.md 12.3 already
/// documented for <c>/login</c>, fixed there by moving under <c>/account/...</c> instead).
/// </summary>
public static class RepositoryEndpoints
{
    /// <summary>Maps <c>GET /repositories</c> and <c>POST /repositories/clone</c>, both requiring a valid bearer credential.</summary>
    public static IEndpointRouteBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/repositories", HandleListAsync)
            .AddEndpointFilter<BearerAuthenticationFilter>();

        endpoints.MapPost("/repositories/clone", HandleCloneAsync)
            .AddEndpointFilter<BearerAuthenticationFilter>();

        return endpoints;
    }

    /// <summary>
    /// Returns every repository directly under the configured root, with the same metadata
    /// (size, git status, active/idle) the dashboard's Repositories view shows - a cache read
    /// (<see cref="RepositoryStatsCache"/> via <see cref="IRepositoryManager.ListAsync"/>), not a
    /// live filesystem/git walk, so this is cheap to call.
    /// </summary>
    private static async Task<IResult> HandleListAsync(IRepositoryManager repositoryManager, CancellationToken cancellationToken) =>
        Results.Ok(await repositoryManager.ListAsync(cancellationToken));

    private static async Task<IResult> HandleCloneAsync(
        CloneRepositoryRequest body, IRepositoryManager repositoryManager, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Url) || string.IsNullOrWhiteSpace(body.Name))
        {
            return Results.BadRequest(new { reason = "validation" });
        }

        var result = await repositoryManager.CloneAsync(body.Url, body.Name, cancellationToken);

        switch (result)
        {
            case RepositoryActionResult.Accepted accepted:
                // Same X-Run-Id-header convention every other run-starting endpoint uses, plus the
                // id in the body too - unlike the SSE endpoints, nothing here needs the header to
                // arrive before a streamed body, so there's no ordering reason to pick just one.
                httpContext.Response.Headers["X-Run-Id"] = accepted.RunId.ToString();
                return Results.Accepted(value: new { runId = accepted.RunId });

            case RepositoryActionResult.Rejected { Reason: RepositoryActionRejectionReason.InvalidName }:
                return Results.BadRequest(new { reason = "validation" });

            case RepositoryActionResult.Rejected { Reason: RepositoryActionRejectionReason.AlreadyExists }:
                return Results.Conflict(new { reason = "already-exists" });

            case RepositoryActionResult.Rejected { Reason: RepositoryActionRejectionReason.Busy } rejected:
                return Results.Conflict(new { reason = "busy", runId = rejected.ConflictingRunId });

            default:
                // RepositoryActionRejectionReason.NotFound is delete-only and never returned by
                // CloneAsync - unreachable, but the switch has to be exhaustive over the sealed
                // hierarchy's shape rather than every individual reason value.
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
