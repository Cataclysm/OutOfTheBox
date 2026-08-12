// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>
/// A remote host an operator or MCP caller has explicitly authorized a git credential for, via
/// <c>authorize_git_host</c> or the dashboard's change-credential action - answers "what's
/// configured," not "is it currently working" (see <see cref="GitHostCredentialHealth"/> for that).
/// Deliberately holds no username (PAT-only auth - see design.md's "no username parameter" decision).
/// <paramref name="EncryptedToken"/> is a machine-scoped-DPAPI-encrypted copy of the token, alongside
/// (not instead of) the copy written into git's own credential helper - the two independently-durable
/// stores this service's <c>CredentialSyncService</c> keeps in sync, since the credential-helper copy
/// alone was confirmed not to survive a plain uninstall-then-reinstall (the dedicated service account
/// is recreated with a new SID and an empty vault).
/// </summary>
public sealed record GitHostAuthorization(string Host, DateTimeOffset AuthorizedAtUtc, byte[]? EncryptedToken);
