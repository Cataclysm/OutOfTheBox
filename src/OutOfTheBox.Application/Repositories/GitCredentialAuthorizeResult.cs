// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// The outcome of an <see cref="IGitCredentialStore.AuthorizeAsync"/> call - same synchronous,
/// specific-reason-per-failure shape every other result type in this namespace uses, per
/// specs/mcp-git-credentials' "every diagnostic-tool failure reports a specific, actionable reason"
/// requirement.
/// </summary>
public abstract record GitCredentialAuthorizeResult
{
    /// <summary>The credential was stored and its retrievability was verified.</summary>
    public sealed record Succeeded : GitCredentialAuthorizeResult;

    /// <summary>No <c>credential.helper</c> is configured on this system - nothing was attempted.</summary>
    public sealed record NoCredentialHelperConfigured : GitCredentialAuthorizeResult;

    /// <summary>The write appeared to succeed but a subsequent <c>git credential fill</c> for the same host did not return it.</summary>
    public sealed record VerificationFailed : GitCredentialAuthorizeResult;

    /// <summary><c>git.exe</c> could not be started at all.</summary>
    public sealed record GitUnreachable(string ErrorMessage) : GitCredentialAuthorizeResult;
}
