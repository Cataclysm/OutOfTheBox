// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>One entry from a repository's <c>git remote -v</c> output.</summary>
public sealed record RepositoryRemote(string Name, string Url);
