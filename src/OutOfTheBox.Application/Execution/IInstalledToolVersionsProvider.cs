// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

namespace OutOfTheBox.Application.Execution;

/// <summary>Reports the host's installed <c>dotnet</c>/<c>git</c> CLI versions, for the dashboard's Status page.</summary>
public interface IInstalledToolVersionsProvider
{
    /// <summary>Returns the installed tool versions - computed once and cached for the service's lifetime, since they don't change without a restart.</summary>
    Task<InstalledToolVersions> GetVersionsAsync(CancellationToken cancellationToken);
}
