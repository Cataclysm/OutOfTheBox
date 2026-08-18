// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

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
/// <param name="EnvironmentVariables">
/// Extra environment variables set on the child process, or <see langword="null"/> for none (every
/// caller except the <c>dotnet_run</c> spawn path's Azure DevOps Artifacts credential injection - see
/// <see cref="OutOfTheBox.Application.Repositories.INuGetFeedCredentialStore"/> - passes
/// <see langword="null"/>). Exists for the same reason <see cref="StandardInput"/> does: a secret
/// (a PAT, via <c>VSS_NUGET_EXTERNAL_FEED_ENDPOINTS</c>) needs to reach a spawned process without
/// ever being a command-line argument - an environment block isn't exposed via WMI's
/// <c>Win32_Process</c> the way a command line is, though it isn't as fully hidden as stdin.
/// </param>
public sealed record ProcessRunRequest(
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string Executable,
    string? StandardInput = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);
