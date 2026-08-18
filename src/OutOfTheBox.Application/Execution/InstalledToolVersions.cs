// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Execution;

/// <summary>
/// The host's installed <c>dotnet</c>/<c>git</c> CLI versions, as reported by each tool's own
/// <c>--version</c> output - either is <see langword="null"/> if the tool couldn't be invoked (not
/// on <c>PATH</c>, or failed to start).
/// </summary>
public sealed record InstalledToolVersions(string? DotnetVersion, string? GitVersion);
