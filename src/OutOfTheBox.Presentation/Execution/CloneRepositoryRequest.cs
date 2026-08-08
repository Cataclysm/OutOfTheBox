// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Presentation.Execution;

/// <summary>The JSON request body for <c>POST /repositories/clone</c>.</summary>
public sealed record CloneRepositoryRequest(string? Url, string? Name);
