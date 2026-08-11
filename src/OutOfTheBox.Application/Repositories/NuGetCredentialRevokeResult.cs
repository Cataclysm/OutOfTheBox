// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>The outcome of an <see cref="INuGetFeedCredentialStore.RevokeAsync"/> call.</summary>
public abstract record NuGetCredentialRevokeResult
{
    /// <summary>The feed's credential was removed from whichever mechanism backed it, and this service's own record of it.</summary>
    public sealed record Revoked : NuGetCredentialRevokeResult;

    /// <summary>The feed had no stored credential - distinguished from a genuine failure, per specs/mcp-nuget-credentials.</summary>
    public sealed record NothingToRevoke : NuGetCredentialRevokeResult;

    /// <summary>The machine's NuGet configuration could not be read or written (generic-feed mechanism only).</summary>
    public sealed record ConfigurationUnwritable(string ErrorMessage) : NuGetCredentialRevokeResult;
}
