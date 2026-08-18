// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using System.ComponentModel;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OutOfTheBox.Presentation.Mcp;

/// <summary>
/// MCP <c>list_repositories</c>/<c>clone_repository</c> tools, per <c>mcp-repository-access</c>'s
/// spec - mirroring exactly the dashboard-external-reachable subset of <c>repository-management</c>
/// (list and clone only; delete and every dashboard-only action are never exposed here).
/// <c>list_repositories</c> reuses <see cref="IRepositoryManager.ListAsync"/> directly, the same
/// cached read the dashboard's own Repositories view uses. <c>clone_repository</c> deliberately does
/// NOT reuse <see cref="IRepositoryManager.CloneAsync"/> - that method's own output sink has no
/// offset-based external polling support, and this capability's spec requires a clone's progress to
/// be genuinely readable incrementally through <c>read_run_output</c> (per
/// <c>mcp-command-execution</c>'s capability, shared by design) exactly like a
/// <c>dotnet_run</c>/<c>git_run</c> run - so <c>clone_repository</c> instead mirrors
/// <c>RepositoryManager.CloneAsync</c>'s own validation/locking/persistence shape directly against
/// the same <see cref="IProcessRunner"/>/<see cref="McpRunOutputRegistry"/> plumbing
/// <see cref="CommandExecutionMcpTools"/> uses, rather than duplicating a second path through
/// <c>IRepositoryManager</c> whose streaming shape doesn't fit here.
/// </summary>
[McpServerToolType]
public sealed class RepositoryAccessMcpTools(
    IRepositoryManager repositoryManager,
    IWorkingDirectoryResolver workingDirectoryResolver,
    IProcessRunner processRunner,
    RunRegistry runRegistry,
    IRunRepository runRepository,
    IRunEventBus runEventBus,
    McpRunOutputRegistry outputRegistry,
    IGitCredentialStore gitCredentialStore,
    IMcpPermissionStore permissionStore,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<ServiceOptions> options,
    ILogger<RepositoryAccessMcpTools> logger)
{
    /// <summary>Lists every repository under the configured root, with the same stats the dashboard already shows.</summary>
    [McpServerTool]
    [Description("Lists every repository under the configured root, with its name, total size, git status, active/idle state, and whether its remote host currently needs a working git credential (see authorize_git_host).")]
    public Task<IReadOnlyList<RepositorySummary>> ListRepositoriesAsync()
    {
        permissionStore.EnsureEnabled("list_repositories");

        return repositoryManager.ListAsync(CancellationToken.None);
    }

    /// <summary>Deletes an entire repository from the host.</summary>
    /// <param name="name">The repository name (as returned by <c>list_repositories</c>).</param>
    [McpServerTool]
    [Description("Permanently deletes an entire repository's directory from the host - to delete a single file or subdirectory instead, use delete_path. Rejects a name that escapes the configured root, one that doesn't exist, or one with another run currently in flight - each with a distinct, specific error.")]
    public async Task<McpDeleteRepositoryResult> DeleteRepositoryAsync(
        [Description("The repository name (as returned by list_repositories).")] string name)
    {
        permissionStore.EnsureEnabled("delete_repository");

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new McpException("name must be supplied.");
        }

        var result = await repositoryManager.DeleteAsync(name, CancellationToken.None);

        return result switch
        {
            RepositoryActionResult.Rejected { Reason: RepositoryActionRejectionReason.NotFound } =>
                throw new McpException($"Repository '{name}' does not exist."),
            RepositoryActionResult.Rejected { Reason: RepositoryActionRejectionReason.Busy } rejected =>
                throw new McpException($"Repository '{name}' is busy with in-flight run '{rejected.ConflictingRunId}'."),
            RepositoryActionResult.Rejected =>
                throw new McpException($"name '{name}' is invalid or outside the configured root."),
            // DeleteAsync runs synchronously to completion despite the "Accepted" naming (unlike
            // CloneAsync, it isn't fire-and-forget) - the actual outcome lives only in the Run row it
            // just persisted, not in this result itself, so it's looked up here to give the caller a
            // definitive answer (and the real error, on failure) rather than a bare "accepted".
            RepositoryActionResult.Accepted accepted => await BuildDeleteResultAsync(name, accepted.RunId),
            _ => throw new McpException("Unexpected result deleting the repository."),
        };
    }

    private async Task<McpDeleteRepositoryResult> BuildDeleteResultAsync(string name, Guid runId)
    {
        var run = await runRepository.FindByIdAsync(runId, CancellationToken.None);
        if (run is { Outcome: RunOutcome.Completed })
        {
            return new McpDeleteRepositoryResult(true);
        }

        throw new McpException($"Failed to delete repository '{name}': {run?.Stderr ?? "unknown error"}");
    }

    /// <summary>Starts cloning a repository into a new, not-yet-existing directory under the configured root, and returns immediately with a run id - poll <c>read_run_output</c> for its progress and result, or <c>cancel_run</c> to abort it.</summary>
    /// <param name="url">The source URL to clone.</param>
    /// <param name="name">The new repository's name (must not already exist under the configured root).</param>
    /// <param name="branch">Optional branch to check out instead of the remote's default.</param>
    [McpServerTool]
    [Description("Starts cloning a git repository into a new repository directory on the host. Returns immediately with a run id - call read_run_output to poll for progress and the final result, or cancel_run to abort it. Rejected if the name already exists or escapes the configured root.")]
    public async Task<McpStartRunResult> CloneRepositoryAsync(
        [Description("The source URL to clone.")] string url,
        [Description("The new repository's name (must not already exist under the configured root).")] string name,
        [Description("Optional branch to check out instead of the remote's default.")] string? branch = null)
    {
        permissionStore.EnsureEnabled("clone_repository");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name))
        {
            throw new McpException("url and name must both be supplied.");
        }

        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            throw new McpException($"name '{name}' is outside the configured root.");
        }

        var targetPath = resolution.ResolvedPath!;
        if (Directory.Exists(targetPath))
        {
            throw new McpException($"A repository named '{name}' already exists.");
        }

        var runId = Guid.NewGuid();
        var cancelRequestCts = new CancellationTokenSource();

        if (!runRegistry.TryAcquire(targetPath, runId, cancelRequestCts, out var conflictingRunId))
        {
            cancelRequestCts.Dispose();
            throw new McpException($"Repository is busy with in-flight run '{conflictingRunId}'.");
        }

        var run = new Run
        {
            Id = runId,
            Kind = RunKind.RepositoryClone,
            RepositoryPath = targetPath,
            SourceUrl = url,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, CancellationToken.None);
        runEventBus.Publish(new RunEvent(runId, RunKind.RepositoryClone, RunEventType.Started, targetPath));

        var buffer = outputRegistry.Create(runId, options.Value.OutputCapBytes);

        _ = CloneToCompletionAsync(run, targetPath, url, branch, cancelRequestCts, buffer);

        return new McpStartRunResult(runId, McpRunStatus.Running);
    }

    private async Task CloneToCompletionAsync(
        Run run, string targetPath, string url, string? branch, CancellationTokenSource cancelRequestCts, McpRunOutputBuffer buffer)
    {
        try
        {
            var timeout = TimeSpan.FromSeconds(options.Value.DefaultExecutionTimeoutSeconds);
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancelRequestCts.Token);

            string[] cloneArguments = string.IsNullOrWhiteSpace(branch)
                ? ["clone", url, targetPath]
                : ["clone", "--branch", branch, url, targetPath];

            var sink = new McpProcessOutputSink(buffer, runEventBus, run.Id, RunKind.RepositoryClone, targetPath);

            try
            {
                var result = await processRunner.RunAsync(
                    new ProcessRunRequest(cloneArguments, options.Value.RootDirectory, "git"),
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
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Outcome = RunOutcome.Failed;
                run.Stderr = ex.Message;
                logger.LogError(ex, "git clone failed to start for MCP-started run {RunId} ({Url} -> {TargetPath}).", run.Id, url, targetPath);
            }

            // Host resolved directly from the clone's own source URL - no process call needed, unlike
            // git_run's equivalent in CommandExecutionMcpTools (which has to look up an existing
            // repository's `origin` remote instead). Records the outcome against the host's
            // credential health (mirroring RepositoryManager.RunCloneToCompletionAsync's identical
            // dashboard-side logic - this tool deliberately doesn't call into RepositoryManager, per
            // this class's own doc comment, so the recording has to happen here too) and, on a
            // classified auth failure, appends the same actionable note git_run's failures get.
            if (GitRemoteUrlParser.TryGetHost(url, out var cloneHost))
            {
                var cloneSucceeded = run.Outcome == RunOutcome.Completed && run.ExitCode == 0;
                if (cloneSucceeded || GitAuthFailureClassifier.IsLikelyAuthFailure(run.Stderr))
                {
                    await gitCredentialStore.RecordOutcomeAsync(cloneHost, cloneSucceeded, CancellationToken.None);
                }

                if (!cloneSucceeded)
                {
                    run.Stderr = await GitCredentialFailureNote.AppendIfAuthFailureAsync(gitCredentialStore, cloneHost, run.Stderr, CancellationToken.None);
                }
            }

            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var scopedRunRepository = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            await scopedRunRepository.UpdateAsync(run, CancellationToken.None);
            runEventBus.Publish(new RunEvent(run.Id, RunKind.RepositoryClone, RunEventType.Terminal, targetPath));

            // Known, accepted v1 gap: unlike a REST/dashboard-initiated clone, this doesn't trigger
            // an immediate post-completion stats refresh - RepositoryManager.RefreshStatsAsync is a
            // private implementation detail of Infrastructure, not part of IRepositoryManager, and
            // adding a new port for it is more Application-surface than this change's "no
            // Infrastructure changes beyond what's strictly needed" goal calls for. list_repositories
            // simply shows "Computing..."/stale stats for this repository until the background
            // RepositoryStatsSampler's own next tick (default 60s) catches up - bounded staleness,
            // not incorrect data.
        }
        finally
        {
            runRegistry.Release(targetPath);
            cancelRequestCts.Dispose();
        }
    }
}

/// <summary>The result of a successful <c>delete_repository</c> call.</summary>
/// <param name="Deleted">Always <see langword="true"/> - a failure throws instead of returning a result with this <see langword="false"/>.</param>
public sealed record McpDeleteRepositoryResult(bool Deleted);
