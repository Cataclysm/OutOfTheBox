// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// MCP tools for <c>dotnet</c>/<c>git</c> command execution, per <c>mcp-command-execution</c>'s
/// spec: <c>dotnet_run</c>/<c>git_run</c> start a run and return immediately with a run id (never
/// blocking for completion, since MCP tool calls are fundamentally request/response);
/// <c>read_run_output</c> polls it incrementally; <c>cancel_run</c> ends it early - via the same
/// <see cref="RunRegistry"/> lock and <see cref="IProcessRunner"/> every other run kind in this
/// service uses, writing output into an <see cref="McpRunOutputBuffer"/> instead of persisted-only
/// text (see <see cref="McpProcessOutputSink"/>). <c>cancel_run</c> is deliberately kind-agnostic
/// beyond excluding repository deletes (see its own remarks) - it accepts a repository clone's run
/// id too, per design.md's "one shared cancel_run" decision.
/// </summary>
[McpServerToolType]
public sealed class CommandExecutionMcpTools(
    IWorkingDirectoryResolver workingDirectoryResolver,
    IProcessRunner processRunner,
    RunRegistry runRegistry,
    IRunRepository runRepository,
    IRunEventBus runEventBus,
    McpRunOutputRegistry outputRegistry,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<ServiceOptions> options,
    ILogger<CommandExecutionMcpTools> logger)
{
    /// <summary>Starts a <c>dotnet</c> command against a repository on the host and returns immediately with a run id - poll <c>read_run_output</c> for its progress and result.</summary>
    /// <param name="arguments">The full <c>dotnet</c> argument list, e.g. <c>["build"]</c> or <c>["test", "--filter", "MyTests"]</c>.</param>
    /// <param name="workingDirectory">Repository-relative working directory to run in.</param>
    /// <param name="timeoutSeconds">Optional execution timeout override, in seconds; clamped to the configured maximum.</param>
    [McpServerTool]
    [Description("Starts a `dotnet` command against a repository on the host. Returns immediately with a run id - call read_run_output to poll for progress and the final result. Rejected if the same repository already has another dotnet/git run or clone in flight.")]
    public Task<McpStartRunResult> DotnetRunAsync(
        [Description("The full `dotnet` argument list, e.g. [\"build\"] or [\"test\", \"--filter\", \"MyTests\"].")] IReadOnlyList<string> arguments,
        [Description("Repository-relative working directory to run in.")] string workingDirectory,
        [Description("Optional execution timeout override, in seconds; clamped to the configured maximum.")] int? timeoutSeconds = null) =>
        StartRunAsync("dotnet", RunKind.DotnetCommand, arguments, workingDirectory, timeoutSeconds);

    /// <summary>Starts a <c>git</c> command against a repository on the host and returns immediately with a run id - poll <c>read_run_output</c> for its progress and result. No subcommand is restricted.</summary>
    /// <param name="arguments">The full <c>git</c> argument list, e.g. <c>["status"]</c> or <c>["log", "--oneline", "-10"]</c>.</param>
    /// <param name="workingDirectory">Repository-relative working directory to run in.</param>
    /// <param name="timeoutSeconds">Optional execution timeout override, in seconds; clamped to the configured maximum.</param>
    [McpServerTool]
    [Description("Starts a `git` command against a repository on the host - no subcommand is restricted. Returns immediately with a run id - call read_run_output to poll for progress and the final result. Rejected if the same repository already has another dotnet/git run or clone in flight.")]
    public Task<McpStartRunResult> GitRunAsync(
        [Description("The full `git` argument list, e.g. [\"status\"] or [\"log\", \"--oneline\", \"-10\"].")] IReadOnlyList<string> arguments,
        [Description("Repository-relative working directory to run in.")] string workingDirectory,
        [Description("Optional execution timeout override, in seconds; clamped to the configured maximum.")] int? timeoutSeconds = null) =>
        StartRunAsync("git", RunKind.GitCommand, arguments, workingDirectory, timeoutSeconds);

    /// <summary>Reads output produced since <paramref name="offset"/> for a run started by <c>dotnet_run</c>, <c>git_run</c>, or <c>clone_repository</c>, plus its current status.</summary>
    /// <param name="runId">A run id returned by <c>dotnet_run</c>, <c>git_run</c>, or <c>clone_repository</c>.</param>
    /// <param name="offset">The offset to read from - 0 for the beginning, or a previous call's <c>nextOffset</c> to continue from there.</param>
    [McpServerTool]
    [Description("Reads output produced since the given offset for a run started by dotnet_run, git_run, or clone_repository, plus its current status. Safe to call repeatedly, including after the run has finished - pass 0 as the offset for the first call, then each result's nextOffset for the next.")]
    public async Task<McpReadRunOutputResult> ReadRunOutputAsync(
        [Description("A run id returned by dotnet_run, git_run, or clone_repository.")] Guid runId,
        [Description("The offset to read from - 0 for the beginning, or a previous call's nextOffset to continue from there.")] long offset = 0)
    {
        var run = await runRepository.FindByIdAsync(runId, CancellationToken.None)
            ?? throw new McpException($"Unknown run id '{runId}'.");

        var buffer = outputRegistry.TryGet(runId);
        var page = buffer?.ReadSince(offset) ?? new McpRunOutputPage(string.Empty, string.Empty, offset, run.Truncated);

        return new McpReadRunOutputResult(
            McpRunStatus.FromOutcome(run.Outcome),
            page.Stdout,
            page.Stderr,
            page.NextOffset,
            page.Truncated,
            run.ExitCode);
    }

    /// <summary>
    /// Cancels an in-flight run started by <c>dotnet_run</c>, <c>git_run</c>, or <c>clone_repository</c>. If the
    /// run has already reached a terminal state, returns that state rather than an error.
    /// </summary>
    /// <param name="runId">The run id to cancel.</param>
    /// <remarks>
    /// Deliberately excludes only repository-delete runs (which no MCP tool ever starts, so a caller
    /// could only name one via a guessed/otherwise-obtained id) - unlike the REST cancel endpoint,
    /// which also excludes repository clones, per design.md's "one shared cancel_run" decision:
    /// MCP's clone_repository funnels into the exact same run id space as dotnet_run/git_run, so
    /// there is no reason to carry that REST-specific restriction forward for MCP. Repository delete
    /// stays excluded because it is, and must remain, reachable only through the authenticated
    /// dashboard - the one destructive action this service has always kept out of the sbx caller's
    /// reach (per repository-management's own security boundary).
    /// </remarks>
    [McpServerTool]
    [Description("Cancels an in-flight run started by dotnet_run, git_run, or clone_repository. If the run already finished, returns its existing status rather than an error.")]
    public async Task<McpCancelRunResult> CancelRunAsync([Description("The run id to cancel.")] Guid runId)
    {
        var run = await runRepository.FindByIdAsync(runId, CancellationToken.None);
        if (run is null || run.Kind == RunKind.RepositoryDelete)
        {
            throw new McpException($"Unknown run id '{runId}'.");
        }

        runRegistry.TryCancel(runId);

        // TryCancel only requests cancellation - the run's background continuation may not have
        // observed and persisted it yet, so re-reading immediately can still show "running". That's
        // expected, not a bug: the caller polls read_run_output to observe the eventual terminal
        // transition, the same way the REST cancel endpoint's Accepted() doesn't wait either.
        var refreshed = await runRepository.FindByIdAsync(runId, CancellationToken.None);
        return new McpCancelRunResult(McpRunStatus.FromOutcome(refreshed?.Outcome ?? run.Outcome));
    }

    private async Task<McpStartRunResult> StartRunAsync(
        string executable, RunKind kind, IReadOnlyList<string>? arguments, string workingDirectory, int? timeoutSeconds)
    {
        if (arguments is null || arguments.Count == 0 || string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new McpException("arguments must be non-empty and workingDirectory must be supplied.");
        }

        var resolution = workingDirectoryResolver.Resolve(workingDirectory);
        if (!resolution.IsAllowed)
        {
            throw new McpException($"workingDirectory '{workingDirectory}' is outside the configured root.");
        }

        var repositoryRoot = resolution.ResolvedPath!;
        var runId = Guid.NewGuid();
        var cancelRequestCts = new CancellationTokenSource();

        if (!runRegistry.TryAcquire(repositoryRoot, runId, cancelRequestCts, out var conflictingRunId))
        {
            cancelRequestCts.Dispose();
            throw new McpException($"Repository is busy with in-flight run '{conflictingRunId}'.");
        }

        var run = new Run
        {
            Id = runId,
            Kind = kind,
            RepositoryPath = repositoryRoot,
            Arguments = arguments,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, CancellationToken.None);
        runEventBus.Publish(new RunEvent(runId, kind, RunEventType.Started, repositoryRoot));

        var buffer = outputRegistry.Create(runId, options.Value.OutputCapBytes);

        // Fire-and-forget, exactly like RepositoryManager.CloneAsync's own background continuation -
        // this tool call returns as soon as the run is accepted, per mcp-command-execution's "Starting
        // a command returns immediately with a run id" requirement; the command keeps running after
        // this MCP request has already completed.
        _ = RunToCompletionAsync(executable, kind, run, repositoryRoot, arguments, timeoutSeconds, cancelRequestCts, buffer);

        return new McpStartRunResult(runId, McpRunStatus.Running);
    }

    private async Task RunToCompletionAsync(
        string executable,
        RunKind kind,
        Run run,
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        int? timeoutSeconds,
        CancellationTokenSource cancelRequestCts,
        McpRunOutputBuffer buffer)
    {
        try
        {
            var timeout = ExecutionTimeoutPolicy.Resolve(
                timeoutSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : null,
                TimeSpan.FromSeconds(options.Value.DefaultExecutionTimeoutSeconds),
                TimeSpan.FromSeconds(options.Value.MaximumExecutionTimeoutSeconds));

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancelRequestCts.Token);

            var sink = new McpProcessOutputSink(buffer, runEventBus, run.Id, kind, repositoryRoot);

            try
            {
                var result = await processRunner.RunAsync(
                    new ProcessRunRequest(arguments, repositoryRoot, executable),
                    sink,
                    linkedCts.Token,
                    onStarted: pid => runRegistry.SetProcessId(run.Id, pid));

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
                run.Outcome = cancelRequestCts.IsCancellationRequested ? RunOutcome.Cancelled : RunOutcome.TimedOut;
            }
            catch (Win32Exception ex)
            {
                // The executable failing to even start (missing/corrupted dotnet or git) - an
                // "operation attempted but failed outside caller control" case. There's no stream to
                // write an error event onto here - the message is captured into Stderr, which
                // read_run_output surfaces the same way it surfaces any other output.
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Outcome = RunOutcome.Failed;
                run.Stderr = ex.Message;
                logger.LogError(ex, "{Executable} failed to start for MCP-started {Kind} run {RunId} against {RepositoryRoot}.", executable, kind, run.Id, repositoryRoot);
            }

            // A fresh scope, not this tool instance's own DI-injected services - this MCP tool call
            // (and the request scope it was resolved in) has already returned to the caller by the
            // time a long-running command finishes, the same reasoning
            // RepositoryManager.RunCloneToCompletionAsync documents for its own background
            // continuation.
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var scopedRunRepository = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            await scopedRunRepository.UpdateAsync(run, CancellationToken.None);
            runEventBus.Publish(new RunEvent(run.Id, kind, RunEventType.Terminal, repositoryRoot));
        }
        finally
        {
            runRegistry.Release(repositoryRoot);
            cancelRequestCts.Dispose();
        }
    }
}
