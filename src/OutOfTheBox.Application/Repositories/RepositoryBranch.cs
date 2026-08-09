// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>One branch entry for the detail subpage's branch-switch dropdown.</summary>
/// <param name="Name">The branch's short name (e.g. <c>main</c>), without a remote prefix even when <see cref="IsRemote"/>.</param>
/// <param name="IsRemote">Whether this is a remote-tracking branch with no corresponding local branch.</param>
/// <param name="IsCurrent">Whether this is the repository's currently checked-out branch.</param>
public sealed record RepositoryBranch(string Name, bool IsRemote, bool IsCurrent);
