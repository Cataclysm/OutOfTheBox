// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// One <c>list_authorized_git_hosts</c> result entry - <see cref="GitHostAuthorization"/> (what's
/// configured) joined with <see cref="GitHostCredentialHealth"/> (whether it currently works), per
/// specs/mcp-git-credentials' "list_authorized_git_hosts reports configured hosts, their health,
/// and no tokens" requirement.
/// </summary>
public sealed record GitHostAuthorizationSummary(string Host, DateTimeOffset AuthorizedAtUtc, bool NeedsCredential);
