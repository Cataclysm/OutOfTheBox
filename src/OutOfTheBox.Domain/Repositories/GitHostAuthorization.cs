// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// A remote host an operator or MCP caller has explicitly authorized a git credential for, via
/// <c>authorize_git_host</c> or the dashboard's change-credential action - answers "what's
/// configured," not "is it currently working" (see <see cref="GitHostCredentialHealth"/> for that).
/// Deliberately holds no username (PAT-only auth - see design.md's "no username parameter"
/// decision) and never the token itself, which this service writes once into the git credential
/// helper and never reads back.
/// </summary>
public sealed record GitHostAuthorization(string Host, DateTimeOffset AuthorizedAtUtc);
