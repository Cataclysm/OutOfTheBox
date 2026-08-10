// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Domain.Repositories;

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// The outcome of a <see cref="IRepositoryFileBrowser.FindEntriesAsync"/> call - the matched
/// entries, plus whether the configured result cap (<c>ServiceOptions.McpMaxFindFilesResults</c>)
/// was reached, the same "cap + visible truncation flag" shape <c>OutputCapBytes</c>/
/// <c>McpMaxFileTransferBytes</c> already use elsewhere, so a caller can tell an incomplete result
/// apart from a genuinely complete one.
/// </summary>
/// <param name="Entries">Every matched file/folder, up to the configured cap.</param>
/// <param name="Truncated">Whether more entries matched than the configured cap allowed returning.</param>
public sealed record RepositoryEntrySearchResult(IReadOnlyList<RepositoryEntryMatch> Entries, bool Truncated);
