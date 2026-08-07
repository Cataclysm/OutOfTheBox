// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace BuildAndTestService.Application.Execution;

/// <summary>A request to run <c>dotnet</c> with a specific argument list in a specific (already-confined) working directory.</summary>
public sealed record ProcessRunRequest(IReadOnlyList<string> Arguments, string WorkingDirectory);
