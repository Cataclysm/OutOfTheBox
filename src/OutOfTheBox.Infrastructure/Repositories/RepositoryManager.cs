// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Concurrency;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Events;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Persistence;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IRepositoryManager" />
/// <remarks>
/// Registered scoped (it depends on the scoped <see cref="IRunRepository"/>), called directly from
/// Blazor component code-behind - never through an HTTP endpoint, per specs/repository-management's
/// "reachable only from the authenticated dashboard" requirement. <see cref="CloneAsync"/>'s
/// background continuation resolves its own <see cref="IRunRepository"/> from a fresh
/// <see cref="IServiceScopeFactory"/>-created scope rather than closing over the caller's injected
/// instance - the calling Blazor circuit's scope (and its <c>DbContext</c>) may not outlive the
/// clone, which keeps running after <see cref="CloneAsync"/> itself has already returned.
/// </remarks>
public sealed class RepositoryManager(
    IWorkingDirectoryResolver workingDirectoryResolver,
    RunRegistry runRegistry,
    IRunRepository runRepository,
    IRunEventBus runEventBus,
    IProcessRunner processRunner,
    IRepositoryStatsProvider statsProvider,
    RepositoryStatsCache statsCache,
    IRepositoryStatsEventBus statsEventBus,
    IGitCredentialStore gitCredentialStore,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<ServiceOptions> options,
    ILogger<RepositoryManager> logger) : IRepositoryManager
{
    // Unit/record separator control characters delimit ListCommitsAsync's `git log --format` fields -
    // see that method's own remarks for why, not a printable delimiter a commit subject could contain.
    private const string LogFieldSeparator = "\x1f";
    private const string LogRecordSeparator = "\x1e";

    /// <inheritdoc />
    public Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken cancellationToken)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<RepositorySummary>>([]);
        }

        var summaries = new List<RepositorySummary>();

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            var resolvedPath = Path.GetFullPath(directory);
            var stats = statsCache.TryGet(name);
            var isActive = runRegistry.IsHeld(resolvedPath);

            summaries.Add(stats is null
                ? new RepositorySummary { Name = name, Path = resolvedPath, StatsComputed = false, IsActive = isActive }
                : new RepositorySummary
                {
                    Name = name,
                    Path = resolvedPath,
                    StatsComputed = true,
                    TotalSizeBytes = stats.TotalSizeBytes,
                    IsGitRepository = stats.IsGitRepository,
                    Branch = stats.Branch,
                    IsDirty = stats.IsDirty,
                    AheadCount = stats.AheadCount,
                    BehindCount = stats.BehindCount,
                    IsRemoteGone = stats.IsRemoteGone,
                    Remotes = [.. stats.Remotes.Select(r => new Domain.Repositories.RepositoryRemote(r.Name, r.Url))],
                    IsDetachedHead = stats.IsDetachedHead,
                    IsActive = isActive,
                    NeedsCredential = stats.NeedsCredential,
                });
        }

        return Task.FromResult<IReadOnlyList<RepositorySummary>>(summaries);
    }

    /// <inheritdoc />
    public async Task<RepositoryActionResult> CloneAsync(string url, string name, string? branch, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var targetPath = resolution.ResolvedPath!;

        if (Directory.Exists(targetPath))
        {
            var now = DateTimeOffset.UtcNow;
            await runRepository.AddAsync(new Run
            {
                Id = Guid.NewGuid(),
                Kind = RunKind.RepositoryClone,
                RepositoryPath = targetPath,
                SourceUrl = url,
                StartedAt = now,
                CompletedAt = now,
                Outcome = RunOutcome.AlreadyExists,
            }, cancellationToken);

            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.AlreadyExists);
        }

        var runId = Guid.NewGuid();
        var cancelRequestCts = new CancellationTokenSource();

        if (!runRegistry.TryAcquire(targetPath, runId, cancelRequestCts, out var conflictingRunId))
        {
            cancelRequestCts.Dispose();
            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
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
        await runRepository.AddAsync(run, cancellationToken);
        runEventBus.Publish(new RunEvent(runId, RunKind.RepositoryClone, RunEventType.Started, targetPath));

        // Fire-and-forget: the operator's click shouldn't block for the clone's full duration - it
        // returns as soon as the run is accepted and keeps going in the background, visible via
        // Status/History like any other run, per specs/service-dashboard's "the clone starts,
        // appears as an in-flight run" requirement.
        _ = RunCloneToCompletionAsync(run, targetPath, url, branch, cancelRequestCts);

        return new RepositoryActionResult.Accepted(runId);
    }

    /// <inheritdoc />
    public async Task<RepositoryActionResult> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var targetPath = resolution.ResolvedPath!;

        if (!Directory.Exists(targetPath))
        {
            var now = DateTimeOffset.UtcNow;
            await runRepository.AddAsync(new Run
            {
                Id = Guid.NewGuid(),
                Kind = RunKind.RepositoryDelete,
                RepositoryPath = targetPath,
                StartedAt = now,
                CompletedAt = now,
                Outcome = RunOutcome.NotFound,
            }, cancellationToken);

            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.NotFound);
        }

        var runId = Guid.NewGuid();
        var cancelRequestCts = new CancellationTokenSource();

        // Acquire, not just check-then-act: closes the TOCTOU race a plain existence/lock check
        // would leave open between "confirmed idle" and "delete actually ran," per design.md.
        if (!runRegistry.TryAcquire(targetPath, runId, cancelRequestCts, out var conflictingRunId))
        {
            cancelRequestCts.Dispose();
            return new RepositoryActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
        }

        var run = new Run
        {
            Id = runId,
            Kind = RunKind.RepositoryDelete,
            RepositoryPath = targetPath,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = RunOutcome.Running,
        };
        await runRepository.AddAsync(run, cancellationToken);
        runEventBus.Publish(new RunEvent(runId, RunKind.RepositoryDelete, RunEventType.Started, targetPath));

        try
        {
            // See RecursiveDelete's own remarks for the two real Windows failure modes this works
            // around (read-only files, and a brief transient "in use" on the directory itself even
            // after every file under it is gone) - a genuinely locked file (still open elsewhere
            // well past the retry window) still fails below, now with the actual reason captured
            // instead of just a bare "it failed."
            await RecursiveDelete.DirectoryAsync(targetPath, cancellationToken);
            run.Outcome = RunOutcome.Completed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked file (still open in another process) or a permission problem the read-only
            // clear above didn't cover - found on real-machine use: without this catch,
            // Directory.Delete's exception propagated straight out of this method (only a `finally`
            // existed, no `catch`), which still ran and set CompletedAt but never reached the line
            // above that sets Outcome, leaving the persisted row in the contradictory
            // Running-but-completed state that surfaced the bug - and nothing was actually deleted,
            // silently. The exception's own message is captured into Stderr so an operator can see
            // *why* it failed, not just that it did.
            run.Outcome = RunOutcome.Failed;
            run.Stderr = ex.Message;
        }
        finally
        {
            run.CompletedAt = DateTimeOffset.UtcNow;
            await runRepository.UpdateAsync(run, CancellationToken.None);
            runEventBus.Publish(new RunEvent(runId, RunKind.RepositoryDelete, RunEventType.Terminal, targetPath));

            statsCache.Remove(name);
            runRegistry.Release(targetPath);
            cancelRequestCts.Dispose();
        }

        return new RepositoryActionResult.Accepted(runId);
    }

    /// <inheritdoc />
    public Task<RepositoryGitActionResult> PullAsync(string name, CancellationToken cancellationToken) =>
        RunGitActionAsync(name, ["pull"], touchesNetwork: true, cancellationToken);

    /// <inheritdoc />
    public Task<RepositoryGitActionResult> PushAsync(string name, CancellationToken cancellationToken) =>
        RunGitActionAsync(name, ["push"], touchesNetwork: true, cancellationToken);

    /// <inheritdoc />
    public Task<RepositoryGitActionResult> ForcePushAsync(string name, CancellationToken cancellationToken) =>
        RunGitActionAsync(name, ["push", "--force"], touchesNetwork: true, cancellationToken);

    /// <inheritdoc />
    public Task<RepositoryGitActionResult> FetchAsync(string name, CancellationToken cancellationToken) =>
        RunGitActionAsync(name, ["fetch"], touchesNetwork: true, cancellationToken);

    /// <inheritdoc />
    public Task<RepositoryGitActionResult> CleanAsync(string name, CancellationToken cancellationToken) =>
        RunGitActionAsync(name, ["clean", "-xdf"], touchesNetwork: false, cancellationToken);

    /// <inheritdoc />
    public async Task<RepositoryGitActionResult> RenameAsync(string name, string newName, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var targetPath = resolution.ResolvedPath!;
        if (!Directory.Exists(targetPath))
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.NotFound);
        }

        var newResolution = workingDirectoryResolver.Resolve(newName);
        if (!newResolution.IsAllowed)
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var newPath = newResolution.ResolvedPath!;
        if (Directory.Exists(newPath))
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.AlreadyExists);
        }

        var runId = Guid.NewGuid();
        using var cancelRequestCts = new CancellationTokenSource();
        if (!runRegistry.TryAcquire(targetPath, runId, cancelRequestCts, out var conflictingRunId))
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
        }

        try
        {
            Directory.Move(targetPath, newPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same class of failure Directory.Delete already has to defend against (a file open
            // elsewhere) - Directory.Move additionally fails across different volumes, surfaced here
            // as a plain IOException too, with its own message.
            return new RepositoryGitActionResult.Failed(ex.Message);
        }
        finally
        {
            runRegistry.Release(targetPath);
        }

        // Run history is keyed by the repository's resolved absolute path (RunQuery.RepositoryPath),
        // not its name - without this, every run recorded before the rename (including the clone run
        // GetCloneSourceUrlAsync reads its source URL from) would silently stop matching under the
        // new name, since the directory move above doesn't touch already-persisted rows.
        await runRepository.UpdateRepositoryPathAsync(targetPath, newPath, cancellationToken);

        statsCache.Remove(name);

        // Primed immediately under the new name rather than left for the next sampler sweep -
        // otherwise the renamed row would sit showing "Computing…" until that tick, the exact
        // regression already fixed once for clone completion (see RefreshStatsAsync's own remarks).
        await RefreshStatsAsync(newName, newPath);

        return new RepositoryGitActionResult.Succeeded();
    }

    /// <inheritdoc />
    public async Task<string?> GetCloneSourceUrlAsync(string name, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            return null;
        }

        var runs = await runRepository.ListAsync(
            new RunQuery { RepositoryPath = resolution.ResolvedPath, Kinds = [RunKind.RepositoryClone], Outcomes = [RunOutcome.Completed] },
            cancellationToken);

        // Most-recent-first per IRunRepository.ListAsync - the latest completed clone is the one
        // that actually produced the directory as it exists today.
        return runs.Count > 0 ? runs[0].SourceUrl : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepositoryBranch>> ListBranchesAsync(string name, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed || !Directory.Exists(resolution.ResolvedPath))
        {
            return [];
        }

        var targetPath = resolution.ResolvedPath!;
        var currentBranch = (await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken))?.Trim();
        var localOutput = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["branch", "--format=%(refname:short)"], cancellationToken) ?? string.Empty;
        var remoteOutput = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["branch", "-r", "--format=%(refname:short)"], cancellationToken) ?? string.Empty;

        var localNames = new HashSet<string>(StringComparer.Ordinal);
        var branches = new List<RepositoryBranch>();

        foreach (var branchName in SplitNonEmptyLines(localOutput))
        {
            localNames.Add(branchName);
            branches.Add(new RepositoryBranch(branchName, IsRemote: false, IsCurrent: branchName == currentBranch));
        }

        foreach (var remoteRef in SplitNonEmptyLines(remoteOutput))
        {
            // "origin/feature" -> "feature"; skips the symbolic "origin/HEAD" ref and any remote
            // branch that already has a local tracking branch of the same short name (per
            // specs/repository-management's "switching to a remote branch with no local counterpart"
            // wording - one with a local counterpart is just the local entry above, not listed twice).
            var slashIndex = remoteRef.IndexOf('/');
            if (slashIndex < 0 || remoteRef.EndsWith("/HEAD", StringComparison.Ordinal))
            {
                continue;
            }

            var branchName = remoteRef[(slashIndex + 1)..];
            if (localNames.Contains(branchName))
            {
                continue;
            }

            branches.Add(new RepositoryBranch(branchName, IsRemote: true, IsCurrent: false));
        }

        return branches;
    }

    /// <inheritdoc />
    public async Task<RepositoryGitActionResult> SwitchBranchAsync(string name, string branch, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var targetPath = resolution.ResolvedPath!;
        if (!Directory.Exists(targetPath))
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.NotFound);
        }

        var runId = Guid.NewGuid();
        using var cancelRequestCts = new CancellationTokenSource();
        if (!runRegistry.TryAcquire(targetPath, runId, cancelRequestCts, out var conflictingRunId))
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
        }

        try
        {
            var localOutput = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["branch", "--format=%(refname:short)"], cancellationToken) ?? string.Empty;
            var hasLocal = SplitNonEmptyLines(localOutput).Contains(branch, StringComparer.Ordinal);

            string[] checkoutArguments;
            if (hasLocal)
            {
                checkoutArguments = ["checkout", branch];
            }
            else
            {
                var remoteRef = await FindRemoteRefAsync(targetPath, branch, cancellationToken);
                if (remoteRef is null)
                {
                    return new RepositoryGitActionResult.Failed($"No local or remote branch named '{branch}'.");
                }

                checkoutArguments = ["checkout", "-b", branch, "--track", remoteRef];
            }

            var sink = new GitCaptureOutputSink();

            try
            {
                var result = await processRunner.RunAsync(new ProcessRunRequest(checkoutArguments, targetPath, "git"), sink, cancellationToken);
                if (result.ExitCode != 0)
                {
                    return new RepositoryGitActionResult.Failed(sink.Stderr);
                }
            }
            catch (Win32Exception ex)
            {
                logger.LogError(ex, "git checkout failed to start for repository '{Name}' switching to branch '{Branch}'.", name, branch);
                return new RepositoryGitActionResult.Failed(ex.Message);
            }

            await RefreshStatsAsync(name, targetPath);

            return new RepositoryGitActionResult.Succeeded();
        }
        finally
        {
            runRegistry.Release(targetPath);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListRemoteBranchesAsync(string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var sink = new GitCaptureOutputSink();

        try
        {
            var result = await processRunner.RunAsync(new ProcessRunRequest(["ls-remote", "--heads", url], options.Value.RootDirectory, "git"), sink, linkedCts.Token);
            if (result.ExitCode != 0)
            {
                return [];
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out - the clone dialog's branch dropdown just stays empty, per specs/
            // repository-management's "Branch enumeration failure does not block cloning"
            // requirement. Not logged: a slow/unreachable remote URL the operator typed is routine,
            // caller-driven behavior, not a bug in this service.
            return [];
        }
        catch (Win32Exception ex)
        {
            // Unlike a timeout, git.exe itself failing to start is this service's own problem, not
            // the remote's - worth a trace, since otherwise it's indistinguishable from "this URL
            // genuinely has zero branches."
            logger.LogWarning(ex, "git ls-remote failed to start while enumerating branches for {Url}.", url);
            return [];
        }

        const string headsPrefix = "refs/heads/";
        var branches = new List<string>();

        foreach (var line in SplitNonEmptyLines(sink.Stdout))
        {
            var tabIndex = line.IndexOf('\t');
            if (tabIndex < 0)
            {
                continue;
            }

            var refName = line[(tabIndex + 1)..].Trim();
            if (refName.StartsWith(headsPrefix, StringComparison.Ordinal))
            {
                branches.Add(refName[headsPrefix.Length..]);
            }
        }

        return branches;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommitSummary>> ListCommitsAsync(string name, int skip, int take, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed || !Directory.Exists(resolution.ResolvedPath))
        {
            return [];
        }

        var targetPath = resolution.ResolvedPath!;
        var remoteNames = await GetRemoteNamesAsync(targetPath, cancellationToken);

        // %x1f/%x1e (unit/record separator control characters) delimit fields/records instead of a
        // printable character like a comma or pipe, which a commit subject could legitimately
        // contain. %at (author date as a Unix timestamp) is used instead of a formatted date string
        // so parsing needs no locale/format assumptions.
        var format = $"%H{LogFieldSeparator}%h{LogFieldSeparator}%P{LogFieldSeparator}%an{LogFieldSeparator}%at{LogFieldSeparator}%D{LogFieldSeparator}%s{LogRecordSeparator}";
        var output = await GitCaptureRunner.CaptureAsync(
            processRunner,
            logger,
            targetPath,
            ["log", "--all", "--topo-order", $"--skip={skip}", $"-n{take}", $"--format={format}"],
            cancellationToken);

        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var commits = new List<CommitSummary>();

        foreach (var record in output.Split(LogRecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split(LogFieldSeparator);
            if (fields.Length < 7)
            {
                continue;
            }

            var hash = fields[0].Trim();
            var shortHash = fields[1].Trim();
            var parents = fields[2].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var authorName = fields[3].Trim();
            var authorDate = long.TryParse(fields[4].Trim(), out var unixSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                : DateTimeOffset.UnixEpoch;
            var refs = GitDecorationParser.Parse(fields[5].Trim(), remoteNames);
            var subject = fields[6].Trim();

            commits.Add(new CommitSummary(hash, shortHash, parents, authorName, authorDate, subject, refs));
        }

        return commits;
    }

    /// <inheritdoc />
    public async Task<CommitDetail?> GetCommitDetailAsync(string name, string hash, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed || !Directory.Exists(resolution.ResolvedPath))
        {
            return null;
        }

        var targetPath = resolution.ResolvedPath!;
        var remoteNames = await GetRemoteNamesAsync(targetPath, cancellationToken);

        // Same %x1f/%x1e delimiter trick as ListCommitsAsync - %b (the message body) can itself
        // contain newlines, but never the unit-separator control character, so splitting on it stays
        // safe even for a multi-paragraph commit message. --name-status appended after --format is
        // git's own documented way to get a formatted header followed by a blank line and then
        // per-file "STATUS<TAB>PATH" lines (or "R100<TAB>OLD<TAB>NEW" for a rename/copy) - --no-patch
        // can't be combined with --name-status (git rejects that combination outright), so the header
        // record separator is what marks where the header ends and the file list begins instead.
        var format = $"%H{LogFieldSeparator}%h{LogFieldSeparator}%P{LogFieldSeparator}%an{LogFieldSeparator}%ae{LogFieldSeparator}%at{LogFieldSeparator}%cn{LogFieldSeparator}%ce{LogFieldSeparator}%ct{LogFieldSeparator}%D{LogFieldSeparator}%s{LogFieldSeparator}%b{LogRecordSeparator}";
        var output = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["show", $"--format={format}", "--name-status", hash], cancellationToken);

        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        var recordSeparatorIndex = output.IndexOf(LogRecordSeparator, StringComparison.Ordinal);
        if (recordSeparatorIndex < 0)
        {
            return null;
        }

        var fields = output[..recordSeparatorIndex].Split(LogFieldSeparator);
        if (fields.Length < 12)
        {
            return null;
        }

        // A second invocation, not combined with the one above - --numstat and --name-status are
        // both diff-format selectors and git rejects using both at once, the same reason --no-patch
        // couldn't be combined with --name-status either (see that comment above). Looked up by path
        // below rather than parsed inline with the name-status pass, since the two outputs are keyed
        // differently for a rename/copy (name-status splits old/new into separate tab fields;
        // numstat instead reports one path using an "old => new" arrow, which ParseNumstat
        // deliberately doesn't parse - see its own comment for why that's fine here).
        var numstatOutput = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["show", "--numstat", "--format=", hash], cancellationToken);
        var lineStats = string.IsNullOrEmpty(numstatOutput) ? [] : ParseNumstat(numstatOutput);

        var files = ParseNameStatus(output[(recordSeparatorIndex + LogRecordSeparator.Length)..], lineStats);
        var refs = GitDecorationParser.Parse(fields[9].Trim(), remoteNames);

        var parentHashes = fields[2].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parents = parentHashes.Length == 0 ? [] : await GetParentInfosAsync(targetPath, parentHashes, cancellationToken);

        return new CommitDetail(
            Hash: fields[0].Trim(),
            ShortHash: fields[1].Trim(),
            Parents: parents,
            AuthorName: fields[3].Trim(),
            AuthorEmail: fields[4].Trim(),
            AuthorDate: ParseUnixSeconds(fields[5]),
            CommitterName: fields[6].Trim(),
            CommitterEmail: fields[7].Trim(),
            CommitterDate: ParseUnixSeconds(fields[8]),
            Subject: fields[10].Trim(),
            Body: fields[11].Trim(),
            Refs: refs,
            Files: files);
    }

    // One `git log --no-walk` call for every parent at once, not one `git show` per parent - a merge
    // commit has two, but there's no reason to pay two round-trips when a single revision list gives
    // git everything it needs. Looked up by hash into a dictionary and re-projected over
    // parentHashes' own order, rather than trusting the output's order to match the input's - `--no-
    // walk` alone still sorts (reverse chronological) unless given `=unsorted`, and matching by hash
    // is simplest to get right without needing to know the exact contract in every git version.
    private async Task<IReadOnlyList<CommitParentInfo>> GetParentInfosAsync(string targetPath, string[] parentHashes, CancellationToken cancellationToken)
    {
        var format = $"%H{LogFieldSeparator}%h{LogFieldSeparator}%s{LogRecordSeparator}";
        var output = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["log", "--no-walk", $"--format={format}", .. parentHashes], cancellationToken);

        var byHash = new Dictionary<string, CommitParentInfo>();
        if (!string.IsNullOrEmpty(output))
        {
            foreach (var record in output.Split(LogRecordSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var recordFields = record.Split(LogFieldSeparator);
                if (recordFields.Length < 3)
                {
                    continue;
                }

                var hash = recordFields[0].Trim();
                byHash[hash] = new CommitParentInfo(hash, recordFields[1].Trim(), recordFields[2].Trim());
            }
        }

        // A parent this lookup couldn't resolve (the git invocation failed outright, or a shallow
        // clone doesn't have that commit's own data) still gets a usable entry - the hash and a
        // best-effort short form, just without a subject - rather than silently dropping it from the
        // list and making the commit look like it has fewer parents than it does.
        return [.. parentHashes.Select(hash => byHash.TryGetValue(hash, out var info) ? info : new CommitParentInfo(hash, hash[..Math.Min(7, hash.Length)], string.Empty))];
    }

    /// <inheritdoc />
    public async Task<string?> GetCommitFileDiffAsync(string name, string hash, string relativePath, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed || !Directory.Exists(resolution.ResolvedPath))
        {
            return null;
        }

        var targetPath = resolution.ResolvedPath!;

        // --format= (empty pretty-format) suppresses the commit header entirely - the operator
        // already sees author/date/message on the page itself, so repeating it here would just be
        // noise above the diff. "--" before the path is git's own idiom for "everything after this
        // is a pathspec, never a flag" - load-bearing here since relativePath isn't otherwise
        // validated against looking like an option (e.g. a file named "--foo").
        var output = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["show", "--format=", hash, "--", relativePath], cancellationToken);

        return string.IsNullOrEmpty(output) ? null : output;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListDirtyFilePathsAsync(string name, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed || !Directory.Exists(resolution.ResolvedPath))
        {
            return [];
        }

        var targetPath = resolution.ResolvedPath!;
        var output = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["status", "--porcelain"], cancellationToken);

        return string.IsNullOrEmpty(output) ? [] : ParsePorcelainPaths(output);
    }

    // Each line is "XY PATH" (a two-character status code, a space, then the path) or, for a
    // rename/copy, "XY OLD -> NEW" - only the current (new) path is kept, matching what the file
    // tree itself lists (it has no notion of "this file used to be called something else").
    private static IReadOnlyList<string> ParsePorcelainPaths(string output)
    {
        var paths = new List<string>();

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4)
            {
                continue;
            }

            var path = rawLine[3..].Trim();
            var arrowIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                path = path[(arrowIndex + 4)..];
            }

            // git wraps a path containing characters core.quotepath treats as special (by default,
            // any non-ASCII byte, plus a literal quote/backslash) in C-style double quotes - the
            // outer quotes are stripped here since the file tree's own paths never carry them, but
            // the escape sequences git would also apply inside (\", \\, \nnn octal bytes) are
            // deliberately not unescaped - a rare enough case for this filter not to be worth the
            // extra complexity; such a file just won't match the filter, same as if it weren't dirty.
            if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            {
                path = path[1..^1];
            }

            paths.Add(path);
        }

        return paths;
    }

    private static DateTimeOffset ParseUnixSeconds(string field) =>
        long.TryParse(field.Trim(), out var unixSeconds) ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds) : DateTimeOffset.UnixEpoch;

    private static IReadOnlyList<CommitFileChange> ParseNameStatus(string section, IReadOnlyDictionary<string, (int Added, int Removed)> lineStats)
    {
        var files = new List<CommitFileChange>();

        foreach (var rawLine in section.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }

            var kind = parts[0][0] switch
            {
                'A' => CommitFileChangeKind.Added,
                'M' => CommitFileChangeKind.Modified,
                'D' => CommitFileChangeKind.Deleted,
                'R' => CommitFileChangeKind.Renamed,
                'C' => CommitFileChangeKind.Copied,
                _ => CommitFileChangeKind.Other,
            };

            // A rename/copy status line carries both the old and new path ("R100<TAB>old<TAB>new");
            // every other status carries just the one current path.
            var path = kind is CommitFileChangeKind.Renamed or CommitFileChangeKind.Copied && parts.Length >= 3
                ? parts[2]
                : parts[1];
            var oldPath = kind is CommitFileChangeKind.Renamed or CommitFileChangeKind.Copied && parts.Length >= 3
                ? parts[1]
                : null;

            // Naturally absent (TryGetValue misses) for a rename/copy - ParseNumstat deliberately
            // never adds an entry for one, and the commit detail page doesn't show line counts for
            // those kinds anyway (an old/new arrow, not a plain path, is what git's own line-stat
            // output would otherwise key it by).
            var stats = lineStats.TryGetValue(path, out var s) ? s : ((int, int)?)null;

            files.Add(new CommitFileChange(path, kind, oldPath, stats?.Item1, stats?.Item2));
        }

        return files;
    }

    // "<added>\t<removed>\t<path>" per changed file (git show --numstat) - "-\t-\t<path>" for a file
    // with no line-based diff (binary content), which is deliberately left out of the returned
    // dictionary (no meaningful count to show). A rename/copy's path here uses git's "old => new"
    // arrow notation instead of a plain path (occasionally shortened to a common-prefix
    // "dir/{old => new}" form) - also deliberately left unparsed and thus absent from the
    // dictionary, since ParseNameStatus never looks up a rename/copy's path in it anyway.
    private static Dictionary<string, (int Added, int Removed)> ParseNumstat(string section)
    {
        var stats = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

        foreach (var rawLine in section.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 3 || parts[2].Contains(" => ", StringComparison.Ordinal))
            {
                continue;
            }

            if (int.TryParse(parts[0], out var added) && int.TryParse(parts[1], out var removed))
            {
                stats[parts[2]] = (added, removed);
            }
        }

        return stats;
    }

    /// <inheritdoc />
    public async Task<RepositoryGitActionResult> CheckoutCommitAsync(string name, string hash, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var targetPath = resolution.ResolvedPath!;
        if (!Directory.Exists(targetPath))
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.NotFound);
        }

        var runId = Guid.NewGuid();
        using var cancelRequestCts = new CancellationTokenSource();
        if (!runRegistry.TryAcquire(targetPath, runId, cancelRequestCts, out var conflictingRunId))
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
        }

        try
        {
            var sink = new GitCaptureOutputSink();

            try
            {
                // A plain `git checkout <hash>` against a commit (rather than a branch name) is
                // exactly what puts the repository into a detached HEAD state - no separate
                // "--detach" flag needed; GitRepositoryStatsProvider's detached-HEAD detection
                // (via `git symbolic-ref`) picks this up the next time stats are computed, below.
                var result = await processRunner.RunAsync(new ProcessRunRequest(["checkout", hash], targetPath, "git"), sink, cancellationToken);
                if (result.ExitCode != 0)
                {
                    return new RepositoryGitActionResult.Failed(sink.Stderr);
                }
            }
            catch (Win32Exception ex)
            {
                logger.LogError(ex, "git checkout failed to start for repository '{Name}' checking out commit {Hash}.", name, hash);
                return new RepositoryGitActionResult.Failed(ex.Message);
            }

            await RefreshStatsAsync(name, targetPath);

            return new RepositoryGitActionResult.Succeeded();
        }
        finally
        {
            runRegistry.Release(targetPath);
        }
    }

    private async Task<HashSet<string>> GetRemoteNamesAsync(string targetPath, CancellationToken cancellationToken)
    {
        var output = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["remote"], cancellationToken);
        return output is null ? [] : new HashSet<string>(SplitNonEmptyLines(output), StringComparer.Ordinal);
    }

    /// <summary>
    /// Recomputes and caches+publishes stats for the named repository, catching and logging (not
    /// throwing) any failure - shared by every call site that refreshes stats immediately after a
    /// mutating action already succeeded (clone, pull/push/force-push/fetch/clean, branch switch,
    /// commit checkout). A stats-refresh failure here must never make the action itself look like it
    /// failed (the git operation already succeeded on disk by the time this runs), and - for
    /// <see cref="RunCloneToCompletionAsync"/> specifically, a fire-and-forget continuation nothing
    /// awaits - must never silently propagate out and skip the Terminal event publish that follows.
    /// </summary>
    private async Task RefreshStatsAsync(string name, string targetPath)
    {
        try
        {
            var stats = await statsProvider.ComputeAsync(targetPath, CancellationToken.None);
            statsCache.Set(name, stats);
            statsEventBus.Publish(name);
        }
        catch (Exception ex)
        {
            // Logged, not just swallowed - a repository whose stats never update after an action
            // that visibly succeeded is otherwise indistinguishable from "nothing is wrong, it just
            // hasn't ticked yet," which is exactly what made this class of bug hard to diagnose the
            // first time around.
            logger.LogWarning(ex, "Failed to refresh stats for repository '{Name}' at {TargetPath} after a repository action completed.", name, targetPath);
        }
    }

    /// <summary>
    /// Shared implementation for <see cref="PullAsync"/>/<see cref="PushAsync"/>/<see cref="ForcePushAsync"/>/
    /// <see cref="FetchAsync"/>/<see cref="CleanAsync"/> - resolves and confines the name, acquires
    /// the per-repository lock for the duration, runs the given git invocation to completion (no
    /// streamed output, no history record - per specs/repository-management's "Dashboard-only
    /// pull/push/force-push/fetch/clean actions" requirement), and refreshes cached stats on success.
    /// </summary>
    /// <param name="name">The repository's name.</param>
    /// <param name="gitArguments">The git subcommand and its own arguments.</param>
    /// <param name="touchesNetwork">
    /// Whether this invocation touches the remote (pull/push/force-push/fetch) versus purely local
    /// (clean) - gates whether the outcome is recorded against the repository's <c>origin</c> host's
    /// credential health, per specs/repository-management's "A host's needs-credential state
    /// reflects the most recent network-touching git operation against it" requirement.
    /// </param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    private async Task<RepositoryGitActionResult> RunGitActionAsync(string name, string[] gitArguments, bool touchesNetwork, CancellationToken cancellationToken)
    {
        var resolution = workingDirectoryResolver.Resolve(name);
        if (!resolution.IsAllowed)
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.InvalidName);
        }

        var targetPath = resolution.ResolvedPath!;
        if (!Directory.Exists(targetPath))
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.NotFound);
        }

        var runId = Guid.NewGuid();
        using var cancelRequestCts = new CancellationTokenSource();
        if (!runRegistry.TryAcquire(targetPath, runId, cancelRequestCts, out var conflictingRunId))
        {
            return new RepositoryGitActionResult.Rejected(RepositoryActionRejectionReason.Busy, conflictingRunId);
        }

        try
        {
            var timeout = TimeSpan.FromSeconds(options.Value.DefaultExecutionTimeoutSeconds);
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
            var sink = new GitCaptureOutputSink();

            try
            {
                var result = await processRunner.RunAsync(new ProcessRunRequest(gitArguments, targetPath, "git"), sink, linkedCts.Token);
                if (result.ExitCode != 0)
                {
                    if (touchesNetwork)
                    {
                        await RecordCredentialOutcomeAsync(targetPath, sink.Stderr, succeeded: false, cancellationToken);
                    }

                    return new RepositoryGitActionResult.Failed(sink.Stderr);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("git {Arguments} timed out for repository '{Name}'.", string.Join(' ', gitArguments), name);
                return new RepositoryGitActionResult.Failed("Timed out.");
            }
            catch (Win32Exception ex)
            {
                logger.LogError(ex, "git {Arguments} failed to start for repository '{Name}'.", string.Join(' ', gitArguments), name);
                return new RepositoryGitActionResult.Failed(ex.Message);
            }

            if (touchesNetwork)
            {
                await RecordCredentialOutcomeAsync(targetPath, stderr: null, succeeded: true, cancellationToken);
            }

            // Recompute+publish immediately, the same as a clone's completion already does - so the
            // row reflects the result without waiting for the next background sampler tick.
            await RefreshStatsAsync(name, targetPath);

            return new RepositoryGitActionResult.Succeeded();
        }
        finally
        {
            runRegistry.Release(targetPath);
        }
    }

    /// <summary>
    /// Resolves <paramref name="targetPath"/>'s <c>origin</c> host and records a network-touching
    /// git operation's outcome against it, per <see cref="IGitCredentialStore.RecordOutcomeAsync"/>.
    /// A failure is only recorded when <paramref name="stderr"/> classifies as an authentication
    /// failure (an unrelated failure - network down, bad ref - must not misattribute the host's
    /// credential as broken); a success is always recorded regardless of whether the host actually
    /// needed a credential at all (harmless - it just means <c>NeedsCredential</c> stays false).
    /// Best-effort: a host that can't be resolved (no <c>origin</c>, or an unparseable URL) is
    /// silently skipped, not treated as a failure of the action itself.
    /// </summary>
    private async Task RecordCredentialOutcomeAsync(string targetPath, string? stderr, bool succeeded, CancellationToken cancellationToken)
    {
        if (!succeeded && !GitAuthFailureClassifier.IsLikelyAuthFailure(stderr))
        {
            return;
        }

        var originUrl = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["remote", "get-url", "origin"], cancellationToken);
        if (originUrl is null || !GitRemoteUrlParser.TryGetHost(originUrl.Trim(), out var host))
        {
            return;
        }

        await gitCredentialStore.RecordOutcomeAsync(host, succeeded, cancellationToken);
    }

    private async Task<string?> FindRemoteRefAsync(string targetPath, string branch, CancellationToken cancellationToken)
    {
        var remoteOutput = await GitCaptureRunner.CaptureAsync(processRunner, logger, targetPath, ["branch", "-r", "--format=%(refname:short)"], cancellationToken) ?? string.Empty;
        string? fallback = null;

        foreach (var remoteRef in SplitNonEmptyLines(remoteOutput))
        {
            if (!remoteRef.EndsWith($"/{branch}", StringComparison.Ordinal))
            {
                continue;
            }

            if (remoteRef.StartsWith("origin/", StringComparison.Ordinal))
            {
                return remoteRef;
            }

            fallback ??= remoteRef;
        }

        return fallback;
    }

    private static IEnumerable<string> SplitNonEmptyLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

    private async Task RunCloneToCompletionAsync(Run run, string targetPath, string url, string? branch, CancellationTokenSource cancelRequestCts)
    {
        var sink = new RepositoryCloneOutputSink(runEventBus, run.Id, targetPath, options.Value.OutputCapBytes);

        try
        {
            var timeout = TimeSpan.FromSeconds(options.Value.DefaultExecutionTimeoutSeconds);
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancelRequestCts.Token);

            string[] cloneArguments = string.IsNullOrWhiteSpace(branch)
                ? ["clone", url, targetPath]
                : ["clone", "--branch", branch, url, targetPath];

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
                // git.exe failing to even start (missing/corrupted) - same class of "operation
                // attempted but failed outside caller control" as RepositoryManager.DeleteAsync's
                // IOException/UnauthorizedAccessException catch. Without this, the exception would
                // propagate out of this background continuation entirely, skipping the
                // UpdateAsync call below and leaving the run parked at Running forever - this
                // method has no HTTP response to abort onto, so there's no other signal that
                // would ever surface the failure. The message is captured into Stderr the same way
                // DeleteAsync's catch does, so an operator sees why, not just that it failed.
                logger.LogError(ex, "git clone failed to start for run {RunId} ({Url} -> {TargetPath}).", run.Id, url, targetPath);
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.Outcome = RunOutcome.Failed;
                run.Stderr = ex.Message;
            }

            // A fresh scope/DbContext, not the caller's - the Blazor circuit that started this
            // clone may already be gone by the time it finishes.
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var scopedRunRepository = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            await scopedRunRepository.UpdateAsync(run, CancellationToken.None);

            // Recorded before RefreshStatsAsync below, for the same reason RefreshStatsAsync itself
            // runs before the Terminal event - so the stats it recomputes (including NeedsCredential)
            // already reflect this outcome. Host resolved from the clone's own source URL (no git
            // query needed, unlike RunGitActionAsync's pull/push/fetch, which resolve it from an
            // already-existing repository's `origin` remote instead) - a non-Completed outcome
            // (cancelled/timed out/git unreachable) records nothing, same reasoning as
            // RecordCredentialOutcomeAsync's own "unrelated failure must not misattribute the host's
            // credential" comment.
            if (run.Outcome == RunOutcome.Completed && GitRemoteUrlParser.TryGetHost(url, out var cloneHost))
            {
                var cloneSucceeded = run.ExitCode == 0;
                if (cloneSucceeded || GitAuthFailureClassifier.IsLikelyAuthFailure(run.Stderr))
                {
                    var scopedCredentialStore = scope.ServiceProvider.GetRequiredService<IGitCredentialStore>();
                    await scopedCredentialStore.RecordOutcomeAsync(cloneHost, cloneSucceeded, CancellationToken.None);
                }
            }

            // Computed and cached BEFORE the Terminal event below, not after - found on real-machine
            // use: Repositories.razor refreshes exactly once, synchronously, off that event, so
            // publishing it first left the dashboard's one-shot snapshot racing this computation and
            // permanently stuck showing "Computing…" (StatsComputed still false at snapshot time)
            // until some unrelated event elsewhere triggered another refresh. Ordering this first
            // means the snapshot the Terminal event triggers always already has the fresh stats.
            // RefreshStatsAsync catches and logs rather than throwing - critical here specifically,
            // since this whole method is a fire-and-forget continuation (line ~139: `_ =
            // RunCloneToCompletionAsync(...)`, nothing awaits it). Before RefreshStatsAsync existed,
            // an exception here propagated straight out uncaught, silently skipping the Terminal
            // event publish below and leaving the freshly-cloned repository stuck at "Computing…"
            // until RepositoryStatsSampler's own next tick happened to succeed where this didn't -
            // found on real-machine use, the same "stats never update" symptom this codebase has
            // now chased down twice.
            if (run.Outcome == RunOutcome.Completed)
            {
                await RefreshStatsAsync(Path.GetFileName(targetPath), targetPath);
            }

            runEventBus.Publish(new RunEvent(run.Id, RunKind.RepositoryClone, RunEventType.Terminal, targetPath));
        }
        finally
        {
            runRegistry.Release(targetPath);
            cancelRequestCts.Dispose();
        }
    }
}
