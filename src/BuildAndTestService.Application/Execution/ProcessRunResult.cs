// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace BuildAndTestService.Application.Execution;

/// <summary>The result of a process that ran to completion (as opposed to being killed by a timeout/cancellation).</summary>
public sealed record ProcessRunResult(int ExitCode);
