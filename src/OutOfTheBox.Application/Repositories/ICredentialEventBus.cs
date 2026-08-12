// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// In-process publish/subscribe for credential-store changes (git host or NuGet feed, authorize or
/// revoke alike), mirroring <see cref="IRepositoryStatsEventBus"/>'s subscribe/<c>StateHasChanged</c>/
/// unsubscribe pattern. Exists because <see cref="IGitCredentialStore"/>/<see cref="INuGetFeedCredentialStore"/>
/// are mutated identically from two independent callers - the dashboard's own Credentials page and the
/// <c>authorize_git_host</c>/<c>revoke_git_host_authorization</c>/<c>authorize_nuget_feed</c>/
/// <c>revoke_nuget_feed_authorization</c> MCP tools - so an operator with the Credentials page already
/// open has no other way to learn that an MCP-driven change just happened.
/// </summary>
public interface ICredentialEventBus
{
    /// <summary>Publishes that some credential was authorized or revoked, to every current subscriber. Never throws on a subscriber's own failure.</summary>
    void Publish();

    /// <summary>Registers <paramref name="handler"/> to be invoked for every subsequently published change. Dispose the returned handle to unsubscribe.</summary>
    IDisposable Subscribe(Action handler);
}
