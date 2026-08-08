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
/// Maps <c>POST /files</c>, per specs/file-transfer: a plain (non-SSE) authenticated file
/// download, two-level path confinement (repository confined to the configured root, then the requested
/// file confined to that specific repository directory), no per-repository lock, cancellable through the same
/// <c>POST /run/{runId}/cancel</c> endpoint <see cref="RunEndpoints"/> maps. Deliberately not
/// restricted to build-output-shaped paths - any file inside a configured repository can be requested,
/// per the original "artifact transfer" name's own already-inaccurate scope.
/// </summary>
public static class FileTransferEndpoints
{
    /// <summary>Maps <c>POST /files</c>, requiring a valid bearer credential.</summary>
    public static IEndpointRouteBuilder MapFileTransferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/files", HandleTransferAsync)
            .AddEndpointFilter<BearerAuthenticationFilter>();

        return endpoints;
    }

    private static async Task HandleTransferAsync(
        FileTransferRequest body,
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

        if (string.IsNullOrWhiteSpace(body.Repository) || string.IsNullOrWhiteSpace(body.Path))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var repositoryResolution = workingDirectoryResolver.Resolve(body.Repository);
        if (!repositoryResolution.IsAllowed)
        {
            // Per specs/file-transfer's "Requested repository itself is outside the configured
            // root" scenario: rejected the same way an escaping working directory is, without
            // attempting to resolve any file path.
            response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var repositoryRoot = repositoryResolution.ResolvedPath!;

        var fileResolution = workingDirectoryResolver.ResolveWithinRoot(repositoryRoot, body.Path);
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
            Kind = RunKind.FileTransfer,
            RepositoryPath = repositoryRoot,
            FilePath = body.Path,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, CancellationToken.None);
        runEventBus.Publish(new RunEvent(runId, RunKind.FileTransfer, RunEventType.Started, repositoryRoot));

        try
        {
            // Bounds the transfer the same way POST /run's execution timeout bounds a command,
            // regardless of connection health: without this, a transfer whose client connection
            // dies silently (no clean close, so RequestAborted never fires - Kestrel/the OS only
            // detect a truly vanished peer on a subsequent failed write, which never happens if
            // nothing is left to write) would sit at RunOutcome.Running forever. There's no
            // caller-suppliable override here (FileTransferRequest has no timeout field, unlike
            // StartRunRequest) - MaximumExecutionTimeoutSeconds doubles as this transfer's own fixed
            // ceiling, the same outer bound every other run kind already respects.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.MaximumExecutionTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, cancelRequestCts.Token, httpContext.RequestAborted);

            FileStream fileStream;
            try
            {
                fileStream = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file exists (already checked above) but can't actually be opened - locked by
                // another process, or a permission/read-only problem. Deliberately a separate try
                // from the copy below: this is a distinct failure (never even started, not a
                // connection dying mid-transfer) and headers haven't been sent yet at this point,
                // so a real status code can still be returned rather than just ending the
                // connection the way the copy-phase catches below do.
                response.StatusCode = StatusCodes.Status500InternalServerError;
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Outcome = RunOutcome.Failed;
                await runRepository.UpdateAsync(run, CancellationToken.None);
                runEventBus.Publish(new RunEvent(runId, RunKind.FileTransfer, RunEventType.Terminal, repositoryRoot));
                return;
            }

            await using (fileStream)
            {
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = "application/octet-stream";
                response.ContentLength = fileStream.Length;

                try
                {
                    await fileStream.CopyToAsync(response.Body, linkedCts.Token);

                    run.CompletedAt = DateTimeOffset.UtcNow;
                    run.Outcome = RunOutcome.Completed;
                    run.FileSizeBytes = fileStream.Length;
                }
                catch (OperationCanceledException)
                {
                    // Cancelled (explicitly, timed out, or the client disconnected) - the copy just
                    // stops; there's no SSE-style terminal event to write for a plain file response,
                    // the connection ending part-way through is the signal. FileSizeBytes stays
                    // unset per design.md - it's only meaningful for a transfer that actually finished.
                    run.CompletedAt = DateTimeOffset.UtcNow;
                    run.Outcome = timeoutCts.IsCancellationRequested ? RunOutcome.TimedOut : RunOutcome.Cancelled;
                }
                catch (IOException)
                {
                    // A hard connection reset surfaces as IOException/ConnectionResetException, not
                    // OperationCanceledException - see the matching catch in RunEndpoints.cs for the
                    // full explanation (found on real-machine use: this exception type was escaping
                    // uncaught, leaving the run parked at Running even after the timeout fix above
                    // narrowed the window it could happen in).
                    run.CompletedAt = DateTimeOffset.UtcNow;
                    run.Outcome = RunOutcome.Cancelled;
                }

                await runRepository.UpdateAsync(run, CancellationToken.None);
                runEventBus.Publish(new RunEvent(runId, RunKind.FileTransfer, RunEventType.Terminal, repositoryRoot));
            }
        }
        finally
        {
            runRegistry.ReleaseTransfer(runId);
            cancelRequestCts.Dispose();
        }
    }
}
