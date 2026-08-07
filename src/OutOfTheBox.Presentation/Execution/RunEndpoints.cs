// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using OutOfTheBox.Presentation.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Presentation.Execution;

/// <summary>Maps the command-execution HTTP endpoint(s).</summary>
public static class RunEndpoints
{
    /// <summary>
    /// Maps <c>POST /run</c> (<c>dotnet</c>), <c>POST /run/git</c> (<c>git</c>), and
    /// <c>POST /run/{runId}/cancel</c> (shared by both), all requiring a valid bearer credential.
    /// The two run-starting endpoints share every code path except which executable is fixed -
    /// same request contract, same SSE framing, same <see cref="RunRegistry"/> lock (so a
    /// <c>dotnet</c> run and a <c>git</c> run against the same repo contend for the same lock,
    /// bidirectionally), same cancellation plumbing.
    /// </summary>
    public static IEndpointRouteBuilder MapCommandExecutionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/run", (StartRunRequest body, IWorkingDirectoryResolver workingDirectoryResolver, IProcessRunner processRunner, RunRegistry runRegistry, IRunRepository runRepository, IOptions<ServiceOptions> options, HttpContext httpContext) =>
                HandleStartRunAsync("dotnet", RunKind.DotnetCommand, body, workingDirectoryResolver, processRunner, runRegistry, runRepository, options, httpContext))
            .AddEndpointFilter<BearerAuthenticationFilter>();

        endpoints.MapPost("/run/git", (StartRunRequest body, IWorkingDirectoryResolver workingDirectoryResolver, IProcessRunner processRunner, RunRegistry runRegistry, IRunRepository runRepository, IOptions<ServiceOptions> options, HttpContext httpContext) =>
                HandleStartRunAsync("git", RunKind.GitCommand, body, workingDirectoryResolver, processRunner, runRegistry, runRepository, options, httpContext))
            .AddEndpointFilter<BearerAuthenticationFilter>();

        endpoints.MapPost("/run/{runId:guid}/cancel", HandleCancelRunAsync)
            .AddEndpointFilter<BearerAuthenticationFilter>();

        return endpoints;
    }

    private static async Task HandleStartRunAsync(
        string executable,
        RunKind kind,
        StartRunRequest body,
        IWorkingDirectoryResolver workingDirectoryResolver,
        IProcessRunner processRunner,
        RunRegistry runRegistry,
        IRunRepository runRepository,
        IOptions<ServiceOptions> options,
        HttpContext httpContext)
    {
        var runId = Guid.NewGuid();
        var response = httpContext.Response;

        response.Headers["X-Run-Id"] = runId.ToString();
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // Explicitly start the response now so the caller receives the run id as soon as
        // possible, rather than whenever the first SSE event happens to be written (which, for a
        // slow-starting or hung command, could otherwise be a long, unexplained delay).
        await response.StartAsync(httpContext.RequestAborted);

        var writer = new SseWriter(response);

        if (body.Arguments is null || body.Arguments.Count == 0 || string.IsNullOrWhiteSpace(body.WorkingDirectory))
        {
            await writer.WriteErrorAsync("validation", httpContext.RequestAborted);
            return;
        }

        var resolution = workingDirectoryResolver.Resolve(body.WorkingDirectory);
        if (!resolution.IsAllowed)
        {
            await writer.WriteErrorAsync("validation", httpContext.RequestAborted);
            return;
        }

        var repoRoot = resolution.ResolvedPath!;

        // Dedicated to explicit caller cancellation only - kept separate from the timeout source
        // below so that once a cancellation is observed, the code can tell *which* of the two
        // fired and report the correct SSE error reason instead of collapsing both into one.
        var cancelRequestCts = new CancellationTokenSource();

        if (!runRegistry.TryAcquire(repoRoot, runId, cancelRequestCts, out var conflictingRunId))
        {
            cancelRequestCts.Dispose();
            await writer.WriteErrorAsync("validation", httpContext.RequestAborted, conflictingRunId);
            return;
        }

        var run = new Run
        {
            Id = runId,
            Kind = kind,
            RepoPath = repoRoot,
            Arguments = body.Arguments,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, CancellationToken.None);

        try
        {
            var timeout = ExecutionTimeoutPolicy.Resolve(
                body.TimeoutSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : null,
                TimeSpan.FromSeconds(options.Value.DefaultExecutionTimeoutSeconds),
                TimeSpan.FromSeconds(options.Value.MaximumExecutionTimeoutSeconds));

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, cancelRequestCts.Token, httpContext.RequestAborted);

            var sink = new SseProcessOutputSink(writer, options.Value.OutputCapBytes);

            try
            {
                var result = await processRunner.RunAsync(
                    new ProcessRunRequest(body.Arguments, repoRoot, executable),
                    sink,
                    linkedCts.Token);

                await writer.WriteDoneAsync(result.ExitCode, sink.Truncated, httpContext.RequestAborted);

                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Outcome = RunOutcome.Completed;
                run.ExitCode = result.ExitCode;
                run.Stdout = sink.Stdout;
                run.Stderr = sink.Stderr;
                run.Truncated = sink.Truncated;
            }
            catch (OperationCanceledException)
            {
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Stdout = sink.Stdout;
                run.Stderr = sink.Stderr;
                run.Truncated = sink.Truncated;

                if (cancelRequestCts.IsCancellationRequested)
                {
                    run.Outcome = RunOutcome.Cancelled;
                    await writer.WriteErrorAsync("cancelled", CancellationToken.None);
                }
                else if (timeoutCts.IsCancellationRequested)
                {
                    run.Outcome = RunOutcome.TimedOut;
                    await writer.WriteErrorAsync("timeout", CancellationToken.None);
                }
                else
                {
                    // The client disconnected (RequestAborted) - nothing left to write to, but the
                    // process was still killed (same linkedCts) and the run still reached a
                    // terminal state worth recording accurately rather than leaving it Running.
                    run.Outcome = RunOutcome.Cancelled;
                }
            }

            await runRepository.UpdateAsync(run, CancellationToken.None);
        }
        finally
        {
            runRegistry.Release(repoRoot);
            cancelRequestCts.Dispose();
        }
    }

    private static IResult HandleCancelRunAsync(Guid runId, RunRegistry runRegistry) =>
        runRegistry.TryCancel(runId) ? Results.Accepted() : Results.NotFound();
}
