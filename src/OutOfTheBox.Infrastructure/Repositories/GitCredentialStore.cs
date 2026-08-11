// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Configuration;
using OutOfTheBox.Application.Execution;
using OutOfTheBox.Application.Repositories;
using OutOfTheBox.Domain.Repositories;
using OutOfTheBox.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <inheritdoc cref="IGitCredentialStore" />
/// <remarks>
/// Shells out to git's own <c>credential approve</c>/<c>fill</c>/<c>reject</c> protocol rather than
/// talking to Windows Credential Manager directly - see design.md's "storage" decision. Every
/// invocation runs from the configured root directory (these commands are host-scoped, not
/// repository-scoped, so any writable, already-existing directory works). Registered singleton -
/// <see cref="GitRepositoryStatsProvider"/> (itself singleton, consumed by the singleton-lifetime
/// <c>RepositoryStatsSampler</c> background service) depends on this port too, so it resolves its
/// own scoped <see cref="OutOfTheBoxDbContext"/> per call via <see cref="IServiceScopeFactory"/>
/// rather than taking one as a captive constructor dependency - the same pattern
/// <c>RepositoryManager.RunCloneToCompletionAsync</c> already uses for its own fire-and-forget,
/// potentially-outlives-the-caller's-scope database access.
/// </remarks>
public sealed class GitCredentialStore(
    IProcessRunner processRunner,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<ServiceOptions> options,
    ILogger<GitCredentialStore> logger) : IGitCredentialStore
{
    // Any non-empty username works for both GitHub and Azure DevOps when the password is a valid
    // PAT (see design.md's "no username parameter" decision, and its own live-verification caveat -
    // this literal value is asserted, not yet confirmed against a real request). Fixed and never
    // caller-supplied, so the (protocol, host) pair alone is always the credential-store match key.
    private const string PlaceholderUsername = "x-access-token";

    // Matches GitCaptureRunner's own timeout for short-lived, ad-hoc git invocations - approve/reject
    // are pure local storage operations with no network round trip, so this is generous, not tight.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task<GitCredentialAuthorizeResult> AuthorizeAsync(string host, string token, CancellationToken cancellationToken)
    {
        var normalizedHost = Normalize(host);

        string? credentialHelper;
        try
        {
            credentialHelper = (await GitCaptureRunner.CaptureAsync(processRunner, logger, options.Value.RootDirectory, ["config", "--get", "credential.helper"], cancellationToken))?.Trim();
        }
        catch (Win32Exception ex)
        {
            return new GitCredentialAuthorizeResult.GitUnreachable(ex.Message);
        }

        if (string.IsNullOrEmpty(credentialHelper))
        {
            return new GitCredentialAuthorizeResult.NoCredentialHelperConfigured();
        }

        try
        {
            // Forces Git Credential Manager's plain Basic-Auth ("generic") provider for this host,
            // instead of its own host-aware provider selection - for github.com/dev.azure.com
            // specifically, GCM's default provider prefers an interactive OAuth/browser sign-in flow
            // and maintains its own separate cached-account state, entirely independent of the plain
            // credential store `approve`/`fill`/`reject` manage. Confirmed live: without this, an
            // `approve`d PAT did not stop GCM from independently popping its account-picker UI on
            // every subsequent git operation against the host - exactly the risk this project's own
            // design.md flagged as unverified. Setting it ensures exactly one PAT-based credential
            // governs this host with no competing cached identity, and matters even more on the real
            // service host, where an interactive OAuth prompt has no session to display in at all and
            // would otherwise hang or fail unpredictably rather than just being surfaced.
            await GitCaptureRunner.CaptureAsync(processRunner, logger, options.Value.RootDirectory, ["config", "--global", $"credential.https://{normalizedHost}.provider", "generic"], cancellationToken);

            // Rejects any existing entry for this host before approving the new one - `approve`
            // alone is not guaranteed to replace an existing entry rather than duplicate it. That
            // depends entirely on which credential.helper backend is configured: the OS-level
            // `manager`/`wincred` backends overwrite cleanly by key, but the plain `store` file
            // backend (used by this project's own BehaviorTests, and something a real operator could
            // configure just as validly) APPENDS a new line on `approve` instead of replacing one -
            // so re-authorizing without rejecting first can leave a stale old PAT sitting in the file
            // alongside the new one. Reject-then-approve makes "no more than one PAT per host"
            // hold regardless of backend, rather than an assumption specific to one of them. Safe to
            // call unconditionally - rejecting a host with nothing stored is a no-op, the same
            // idempotent behavior RevokeAsync already relies on.
            var rejectInput = $"protocol=https\nhost={normalizedHost}\n\n";
            await RunAndDiscardAsync(["credential", "reject"], rejectInput, cancellationToken);

            var approveInput = $"protocol=https\nhost={normalizedHost}\nusername={PlaceholderUsername}\npassword={token}\n\n";
            await RunAndDiscardAsync(["credential", "approve"], approveInput, cancellationToken);

            var fillInput = $"protocol=https\nhost={normalizedHost}\n\n";
            var fillOutput = await GitCaptureRunner.CaptureAsync(processRunner, logger, options.Value.RootDirectory, ["credential", "fill"], cancellationToken, standardInput: fillInput);

            if (fillOutput is null || !fillOutput.Contains("password=", StringComparison.Ordinal))
            {
                return new GitCredentialAuthorizeResult.VerificationFailed();
            }
        }
        catch (Win32Exception ex)
        {
            return new GitCredentialAuthorizeResult.GitUnreachable(ex.Message);
        }

        await using (var scope = serviceScopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();
            await UpsertAuthorizationAsync(dbContext, normalizedHost, cancellationToken);
        }

        return new GitCredentialAuthorizeResult.Succeeded();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHostAuthorizationSummary>> ListAuthorizedHostsAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var authorizations = await dbContext.GitHostAuthorizations.AsNoTracking().ToListAsync(cancellationToken);
        var health = await dbContext.GitHostCredentialHealth.AsNoTracking().ToDictionaryAsync(h => h.Host, cancellationToken);

        return [.. authorizations
            .OrderBy(a => a.Host, StringComparer.Ordinal)
            .Select(a => new GitHostAuthorizationSummary(a.Host, a.AuthorizedAtUtc, GitHostCredentialHealth.NeedsCredential(health.GetValueOrDefault(a.Host))))];
    }

    /// <inheritdoc />
    public async Task<GitCredentialRevokeResult> RevokeAsync(string host, CancellationToken cancellationToken)
    {
        var normalizedHost = Normalize(host);

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var existing = await dbContext.GitHostAuthorizations.FirstOrDefaultAsync(a => a.Host == normalizedHost, cancellationToken);
        if (existing is null)
        {
            return new GitCredentialRevokeResult.NothingToRevoke();
        }

        try
        {
            var rejectInput = $"protocol=https\nhost={normalizedHost}\n\n";
            await RunAndDiscardAsync(["credential", "reject"], rejectInput, cancellationToken);
        }
        catch (Win32Exception ex)
        {
            return new GitCredentialRevokeResult.GitUnreachable(ex.Message);
        }

        dbContext.GitHostAuthorizations.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Deliberately does NOT proactively mark this host as needing credential (an earlier version
        // of this method did, via RecordOutcomeAsync(normalizedHost, false, ...), and it was wrong -
        // GitHostCredentialHealth is host-scoped, not per-repository, so a host can easily have both
        // private and public repositories cloned from it (a real, reported scenario: one private
        // GitHub repo plus several public ones). Marking the whole host as failing here flags every
        // public repository too, even though nothing about them actually changed - they never needed
        // a credential before this revoke and still don't. NeedsCredential instead stays purely
        // reactive (only ever set by a real pull/push/fetch/clone failure), the same way it already
        // handles every other way a credential can stop working (expiry, revocation on the remote
        // host itself) - a deliberate, already-accepted staleness trade-off (see design.md), not
        // something a same-host revoke should special-case differently. Still refreshes affected
        // repositories' cached stats immediately - this never changes what NeedsCredential evaluates
        // to, only how quickly the dashboard reflects health state that was already true beforehand.
        await RefreshAffectedRepositoriesAsync(normalizedHost, cancellationToken);

        return new GitCredentialRevokeResult.Revoked();
    }

    /// <inheritdoc />
    public async Task<string?> GetCurrentTokenAsync(string host, CancellationToken cancellationToken)
    {
        var normalizedHost = Normalize(host);
        var fillInput = $"protocol=https\nhost={normalizedHost}\n\n";
        var fillOutput = await GitCaptureRunner.CaptureAsync(processRunner, logger, options.Value.RootDirectory, ["credential", "fill"], cancellationToken, standardInput: fillInput);

        return fillOutput is null ? null : ParsePassword(fillOutput);
    }

    // git's credential protocol is line-oriented key=value pairs - the password value is everything
    // after the first '=' on the one line starting "password=", verbatim (a PAT can itself contain
    // '=', e.g. base64-flavored Azure DevOps tokens, so splitting only on the first occurrence matters).
    private static string? ParsePassword(string fillOutput)
    {
        foreach (var line in fillOutput.Split('\n'))
        {
            if (line.StartsWith("password=", StringComparison.Ordinal))
            {
                return line["password=".Length..].TrimEnd('\r');
            }
        }

        return null;
    }

    /// <summary>
    /// Immediately recomputes and publishes the git-status half (which carries <c>NeedsCredential</c>)
    /// for every repository whose <c>origin</c> remote resolves to <paramref name="normalizedHost"/>,
    /// after a credential change for that host - so the dashboard/list_repositories reflect it
    /// without waiting for <c>RepositoryStatsSampler</c>'s own next periodic tick. Resolves
    /// <see cref="IRepositoryManager"/>/<see cref="IRepositoryStatsProvider"/>/<see cref="RepositoryStatsCache"/>/
    /// <see cref="IRepositoryStatsEventBus"/> lazily from a fresh scope rather than as constructor
    /// dependencies - <see cref="OutOfTheBox.Infrastructure.Repositories.GitRepositoryStatsProvider"/>
    /// (the concrete <see cref="IRepositoryStatsProvider"/>) itself depends on this class, so taking
    /// any of these as a captive constructor dependency here would be a circular singleton graph;
    /// resolving them only when this method actually runs sidesteps that entirely, the same reasoning
    /// this class already applies to <see cref="OutOfTheBoxDbContext"/>. Best-effort: a repository
    /// this can't enumerate or recompute for is logged and skipped, never fails the revoke itself.
    /// </summary>
    private async Task RefreshAffectedRepositoriesAsync(string normalizedHost, CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var repositoryManager = scope.ServiceProvider.GetRequiredService<IRepositoryManager>();
        var statsProvider = scope.ServiceProvider.GetRequiredService<IRepositoryStatsProvider>();
        var statsCache = scope.ServiceProvider.GetRequiredService<RepositoryStatsCache>();
        var statsEventBus = scope.ServiceProvider.GetRequiredService<IRepositoryStatsEventBus>();

        IReadOnlyList<RepositorySummary> summaries;
        try
        {
            summaries = await repositoryManager.ListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enumerate repositories to refresh after revoking the credential for '{Host}'.", normalizedHost);
            return;
        }

        foreach (var summary in summaries)
        {
            var origin = summary.Remotes.FirstOrDefault(r => r.Name == "origin");
            if (origin is null || !GitRemoteUrlParser.TryGetHost(origin.Url, out var repoHost) || Normalize(repoHost) != normalizedHost)
            {
                continue;
            }

            try
            {
                var gitStatus = await statsProvider.ComputeGitStatusAsync(summary.Path, cancellationToken);
                statsCache.SetGitStatus(summary.Name, gitStatus);
                statsEventBus.Publish(summary.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not refresh repository '{Repository}' after revoking the credential for '{Host}'.", summary.Name, normalizedHost);
            }
        }
    }

    /// <inheritdoc />
    public async Task RecordOutcomeAsync(string host, bool succeeded, CancellationToken cancellationToken)
    {
        var normalizedHost = Normalize(host);
        var now = DateTimeOffset.UtcNow;

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        var existing = await dbContext.GitHostCredentialHealth.FirstOrDefaultAsync(h => h.Host == normalizedHost, cancellationToken);
        var updated = existing is null
            ? new GitHostCredentialHealth(normalizedHost, succeeded ? null : now, succeeded ? now : null)
            : existing with
            {
                LastAuthFailureAtUtc = succeeded ? existing.LastAuthFailureAtUtc : now,
                LastAuthSuccessAtUtc = succeeded ? now : existing.LastAuthSuccessAtUtc,
            };

        if (existing is null)
        {
            dbContext.GitHostCredentialHealth.Add(updated);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(updated);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GitHostCredentialHealth?> GetHealthAsync(string host, CancellationToken cancellationToken)
    {
        var normalizedHost = Normalize(host);

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutOfTheBoxDbContext>();

        return await dbContext.GitHostCredentialHealth.AsNoTracking().FirstOrDefaultAsync(h => h.Host == normalizedHost, cancellationToken);
    }

    private static async Task UpsertAuthorizationAsync(OutOfTheBoxDbContext dbContext, string normalizedHost, CancellationToken cancellationToken)
    {
        var existing = await dbContext.GitHostAuthorizations.FirstOrDefaultAsync(a => a.Host == normalizedHost, cancellationToken);
        var updated = new GitHostAuthorization(normalizedHost, DateTimeOffset.UtcNow);

        if (existing is null)
        {
            dbContext.GitHostAuthorizations.Add(updated);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(updated);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Hosts are compared/stored lower-invariant rather than relying on a provider-specific
    // collation for case-insensitive matching (see OutOfTheBoxDbContext's own remark).
    private static string Normalize(string host) => host.Trim().ToLowerInvariant();

    /// <summary>
    /// Runs <c>git</c> with <paramref name="standardInput"/> piped in and discards its output -
    /// for <c>approve</c>/<c>reject</c>, whose own exit code/stdout carry no meaningful signal this
    /// class relies on (verification happens via a separate <c>fill</c> call - see
    /// <see cref="AuthorizeAsync"/>). Time-bounded the same way <see cref="GitCaptureRunner"/> is;
    /// <see cref="Win32Exception"/> (git.exe unreachable) is left to propagate to the caller.
    /// </summary>
    private async Task RunAndDiscardAsync(string[] arguments, string standardInput, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        try
        {
            await processRunner.RunAsync(new ProcessRunRequest(arguments, options.Value.RootDirectory, "git", standardInput), NullOutputSink.Instance, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("git {Arguments} timed out.", string.Join(' ', arguments));
        }
    }

    private sealed class NullOutputSink : IProcessOutputSink
    {
        public static readonly NullOutputSink Instance = new();

        public Task OnStandardOutputAsync(string line, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnStandardErrorAsync(string line, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
