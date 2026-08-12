// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using OutOfTheBox.Application.Execution;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Repositories;

/// <summary>
/// The `git credential approve`/`fill`/`reject` protocol calls shared by <see cref="GitCredentialStore"/>
/// (the operator/MCP-facing authorize/revoke path) and <c>CredentialSyncService</c> (the periodic
/// background repair path) - both need to drive the exact same write/verify sequence against git's own
/// credential helper, and duplicating it would risk the two silently drifting.
/// </summary>
internal static class GitCredentialWriter
{
    // Any non-empty username works for both GitHub and Azure DevOps when the password is a valid
    // PAT (see design.md's "no username parameter" decision). Fixed and never caller-supplied, so
    // the (protocol, host) pair alone is always the credential-store match key.
    public const string PlaceholderUsername = "x-access-token";

    // Matches GitCaptureRunner's own timeout for short-lived, ad-hoc git invocations - approve/
    // reject/fill are pure local storage operations with no network round trip, so this is generous,
    // not tight.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Returns the password git's credential helper currently has stored for
    /// <paramref name="normalizedHost"/>, or <see langword="null"/> if none is stored, git.exe is
    /// unreachable, or the call is cancelled.
    /// </summary>
    public static async Task<string?> FillPasswordAsync(IProcessRunner processRunner, ILogger logger, string rootDirectory, string normalizedHost, CancellationToken cancellationToken)
    {
        var fillInput = $"protocol=https\nhost={normalizedHost}\n\n";
        var fillOutput = await GitCaptureRunner.CaptureAsync(processRunner, logger, rootDirectory, ["credential", "fill"], cancellationToken, standardInput: fillInput);
        if (fillOutput is null)
        {
            return null;
        }

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
    /// Sets the "generic" provider override, rejects any existing entry, approves <paramref name="token"/>
    /// for <paramref name="normalizedHost"/>, then verifies by filling it back. Returns
    /// <see langword="true"/> iff a password came back from the verification fill. May throw
    /// <see cref="Win32Exception"/> if git.exe itself is unreachable for the reject/approve steps
    /// (unlike <see cref="FillPasswordAsync"/>, which never throws it) - left to the caller, matching
    /// this codebase's existing convention of surfacing that specific failure distinctly.
    /// </summary>
    public static async Task<bool> ApproveAndVerifyAsync(IProcessRunner processRunner, ILogger logger, string rootDirectory, string normalizedHost, string token, CancellationToken cancellationToken)
    {
        // Forces Git Credential Manager's plain Basic-Auth ("generic") provider for this host,
        // instead of its own host-aware provider selection - see GitCredentialStore's own remarks
        // (moved here verbatim) for why this matters for github.com/dev.azure.com specifically.
        await GitCaptureRunner.CaptureAsync(processRunner, logger, rootDirectory, ["config", "--global", $"credential.https://{normalizedHost}.provider", "generic"], cancellationToken);

        await RejectAsync(processRunner, logger, rootDirectory, normalizedHost, cancellationToken);

        var approveInput = $"protocol=https\nhost={normalizedHost}\nusername={PlaceholderUsername}\npassword={token}\n\n";
        await RunAndDiscardAsync(processRunner, logger, rootDirectory, ["credential", "approve"], approveInput, cancellationToken);

        var filled = await FillPasswordAsync(processRunner, logger, rootDirectory, normalizedHost, cancellationToken);
        return filled is not null;
    }

    /// <summary>
    /// Rejects any existing entry for <paramref name="normalizedHost"/> - safe to call unconditionally,
    /// rejecting a host with nothing stored is a no-op. Shared by <see cref="ApproveAndVerifyAsync"/>
    /// (reject-then-approve, since `approve` alone isn't guaranteed to replace rather than duplicate an
    /// entry - see design.md) and <see cref="GitCredentialStore.RevokeAsync"/> (a bare reject).
    /// </summary>
    public static Task RejectAsync(IProcessRunner processRunner, ILogger logger, string rootDirectory, string normalizedHost, CancellationToken cancellationToken)
    {
        var rejectInput = $"protocol=https\nhost={normalizedHost}\n\n";
        return RunAndDiscardAsync(processRunner, logger, rootDirectory, ["credential", "reject"], rejectInput, cancellationToken);
    }

    /// <summary>
    /// Runs <c>git</c> with <paramref name="standardInput"/> piped in and discards its output -
    /// for <c>approve</c>/<c>reject</c>, whose own exit code/stdout carry no meaningful signal
    /// (verification happens via a separate <c>fill</c> call). Time-bounded the same way
    /// <see cref="GitCaptureRunner"/> is; <see cref="Win32Exception"/> (git.exe unreachable) is left
    /// to propagate to the caller.
    /// </summary>
    private static async Task RunAndDiscardAsync(IProcessRunner processRunner, ILogger logger, string rootDirectory, string[] arguments, string standardInput, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        try
        {
            await processRunner.RunAsync(new ProcessRunRequest(arguments, rootDirectory, "git", standardInput), NullOutputSink.Instance, linkedCts.Token);
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
