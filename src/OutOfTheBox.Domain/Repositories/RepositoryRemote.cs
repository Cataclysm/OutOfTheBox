// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Domain.Repositories;

/// <summary>One of a repository's configured git remotes, shown on its detail subpage.</summary>
public sealed record RepositoryRemote(string Name, string Url);
