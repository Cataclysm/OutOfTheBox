using BuildAndTestService.Application.Concurrency;
using BuildAndTestService.Application.Configuration;
using BuildAndTestService.Application.Execution;
using BuildAndTestService.Domain.Runs;
using BuildAndTestService.Presentation.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BuildAndTestService.Presentation.Execution;

/// <summary>Maps the command-execution HTTP endpoint(s).</summary>
public static class RunEndpoints
{
    /// <summary>Maps <c>POST /run</c>, requiring a valid bearer credential.</summary>
    public static IEndpointRouteBuilder MapCommandExecutionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/run", HandleStartRunAsync)
            .AddEndpointFilter<BearerAuthenticationFilter>();

        return endpoints;
    }

    private static async Task HandleStartRunAsync(
        StartRunRequest body,
        IWorkingDirectoryResolver workingDirectoryResolver,
        IProcessRunner processRunner,
        RunRegistry runRegistry,
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
        if (!runRegistry.TryAcquire(repoRoot, runId, out var conflictingRunId))
        {
            await writer.WriteErrorAsync("validation", httpContext.RequestAborted, conflictingRunId);
            return;
        }

        try
        {
            var timeout = ExecutionTimeoutPolicy.Resolve(
                body.TimeoutSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : null,
                TimeSpan.FromSeconds(options.Value.DefaultExecutionTimeoutSeconds),
                TimeSpan.FromSeconds(options.Value.MaximumExecutionTimeoutSeconds));

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, httpContext.RequestAborted);

            var sink = new SseProcessOutputSink(writer, options.Value.OutputCapBytes);

            try
            {
                var result = await processRunner.RunAsync(
                    new ProcessRunRequest(body.Arguments, repoRoot),
                    sink,
                    linkedCts.Token);

                await writer.WriteDoneAsync(result.ExitCode, sink.Truncated, httpContext.RequestAborted);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                await writer.WriteErrorAsync("timeout", CancellationToken.None);
            }
        }
        finally
        {
            runRegistry.Release(repoRoot);
        }
    }
}
