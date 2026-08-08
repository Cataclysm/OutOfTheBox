// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Presentation.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Presentation.Execution;

/// <summary>
/// Maps <c>POST /artifacts</c>, per specs/artifact-transfer: a plain (non-SSE) authenticated file
/// download, two-level path confinement (repo confined to the configured root, then the requested
/// file confined to that specific repo directory), no per-repo lock, cancellable through the same
/// <c>POST /run/{runId}/cancel</c> endpoint <see cref="RunEndpoints"/> maps.
/// </summary>
public static class ArtifactTransferEndpoints
{
    /// <summary>Maps <c>POST /artifacts</c>, requiring a valid bearer credential.</summary>
    public static IEndpointRouteBuilder MapArtifactTransferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/artifacts", HandleTransferAsync)
            .AddEndpointFilter<BearerAuthenticationFilter>();

        return endpoints;
    }

    private static async Task HandleTransferAsync(
        ArtifactTransferRequest body,
        IWorkingDirectoryResolver workingDirectoryResolver,
        RunRegistry runRegistry,
        IRunRepository runRepository,
        IRunEventBus runEventBus,
        IOptions<ServiceOptions> options,
        HttpContext httpContext)
    {
        var runId = Guid.NewGuid();
        var response = httpContext.Response;

        // Set before any validation, matching POST /run's "caller has the id even if something
        // later goes wrong" pattern - safe to set this early since headers aren't actually sent
        // until the response body starts, so the status code decided below still applies.
        response.Headers["X-Run-Id"] = runId.ToString();

        if (string.IsNullOrWhiteSpace(body.Repo) || string.IsNullOrWhiteSpace(body.Path))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var repoResolution = workingDirectoryResolver.Resolve(body.Repo);
        if (!repoResolution.IsAllowed)
        {
            // Per specs/artifact-transfer's "Requested repository itself is outside the configured
            // root" scenario: rejected the same way an escaping working directory is, without
            // attempting to resolve any file path.
            response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var repoRoot = repoResolution.ResolvedPath!;

        var fileResolution = workingDirectoryResolver.ResolveWithinRoot(repoRoot, body.Path);
        if (!fileResolution.IsAllowed)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var filePath = fileResolution.ResolvedPath!;

        // A directory is rejected the same way a missing file is (per the "No directory listing"
        // requirement) - this endpoint transfers files only, never enumerates a directory's contents.
        if (!File.Exists(filePath))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var cancelRequestCts = new CancellationTokenSource();
        runRegistry.RegisterTransfer(runId, cancelRequestCts);

        var run = new Run
        {
            Id = runId,
            Kind = RunKind.ArtifactTransfer,
            RepoPath = repoRoot,
            ArtifactPath = body.Path,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, CancellationToken.None);
        runEventBus.Publish(new RunEvent(runId, RunKind.ArtifactTransfer, RunEventType.Started, repoRoot));

        try
        {
            // Bounds the transfer the same way POST /run's execution timeout bounds a command,
            // regardless of connection health: without this, a transfer whose client connection
            // dies silently (no clean close, so RequestAborted never fires - Kestrel/the OS only
            // detect a truly vanished peer on a subsequent failed write, which never happens if
            // nothing is left to write) would sit at RunOutcome.Running forever. There's no
            // caller-suppliable override here (ArtifactTransferRequest has no timeout field, unlike
            // StartRunRequest) - MaximumExecutionTimeoutSeconds doubles as this transfer's own fixed
            // ceiling, the same outer bound every other run kind already respects.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.MaximumExecutionTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, cancelRequestCts.Token, httpContext.RequestAborted);

            await using var fileStream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "application/octet-stream";
            response.ContentLength = fileStream.Length;

            try
            {
                await fileStream.CopyToAsync(response.Body, linkedCts.Token);

                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Outcome = RunOutcome.Completed;
                run.ArtifactSizeBytes = fileStream.Length;
            }
            catch (OperationCanceledException)
            {
                // Cancelled (explicitly, timed out, or the client disconnected) - the copy just
                // stops; there's no SSE-style terminal event to write for a plain file response,
                // the connection ending part-way through is the signal. ArtifactSizeBytes stays
                // unset per design.md - it's only meaningful for a transfer that actually finished.
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Outcome = timeoutCts.IsCancellationRequested ? RunOutcome.TimedOut : RunOutcome.Cancelled;
            }

            await runRepository.UpdateAsync(run, CancellationToken.None);
            runEventBus.Publish(new RunEvent(runId, RunKind.ArtifactTransfer, RunEventType.Terminal, repoRoot));
        }
        finally
        {
            runRegistry.ReleaseTransfer(runId);
            cancelRequestCts.Dispose();
        }
    }
}
