// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Execution;

/// <summary>The JSON request body for <c>POST /repositories/clone</c>.</summary>
/// <param name="Branch">Optional - checks out this branch instead of the remote's default when supplied.</param>
public sealed record CloneRepositoryRequest(string? Url, string? Name, string? Branch = null);
