// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace BuildAndTestService.Presentation.Execution;

/// <summary>The JSON request body for <c>POST /run</c>.</summary>
public sealed record StartRunRequest(IReadOnlyList<string>? Arguments, string? WorkingDirectory, int? TimeoutSeconds);
