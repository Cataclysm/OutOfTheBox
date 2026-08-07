// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Execution;

/// <summary>
/// A request to run <paramref name="Executable"/> with a specific argument list in a specific
/// (already-confined) working directory. <paramref name="Executable"/> is always fixed by the
/// calling endpoint (<c>"dotnet"</c> or <c>"git"</c>), never taken from caller input - a request
/// body can only ever supply arguments to a specific, pre-chosen CLI, never name an arbitrary
/// executable.
/// </summary>
public sealed record ProcessRunRequest(IReadOnlyList<string> Arguments, string WorkingDirectory, string Executable);
