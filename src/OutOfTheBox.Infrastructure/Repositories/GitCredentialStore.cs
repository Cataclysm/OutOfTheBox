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

        return new GitCredentialRevokeResult.Revoked();
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
