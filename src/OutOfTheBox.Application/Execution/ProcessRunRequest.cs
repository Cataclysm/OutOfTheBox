// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Execution;

/// <summary>
/// A request to run <paramref name="Executable"/> with a specific argument list in a specific
/// (already-confined) working directory. <paramref name="Executable"/> is always fixed by the
/// calling endpoint (<c>"dotnet"</c> or <c>"git"</c>), never taken from caller input - a request
/// body can only ever supply arguments to a specific, pre-chosen CLI, never name an arbitrary
/// executable.
/// </summary>
/// <param name="Arguments">The CLI arguments to pass, each becoming one literal argv entry.</param>
/// <param name="WorkingDirectory">The already-confined working directory to run in.</param>
/// <param name="Executable">The executable name (<c>"dotnet"</c> or <c>"git"</c>), always fixed by the calling endpoint.</param>
/// <param name="StandardInput">
/// Text piped to the child process's standard input and then closed, or <see langword="null"/> to
/// leave standard input unredirected (every caller except <c>git credential approve</c>/<c>fill</c>/
/// <c>reject</c> - see <see cref="OutOfTheBox.Application.Repositories.IGitCredentialStore"/> -
/// passes <see langword="null"/>). Exists specifically so a secret (a PAT) never has to appear as a
/// command-line argument, which this host's own process-inspection tooling (and any local admin's
/// Task Manager) could otherwise read back off a running process's command line.
/// </param>
public sealed record ProcessRunRequest(IReadOnlyList<string> Arguments, string WorkingDirectory, string Executable, string? StandardInput = null);
