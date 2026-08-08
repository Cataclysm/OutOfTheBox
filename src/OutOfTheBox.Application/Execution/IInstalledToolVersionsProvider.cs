// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Execution;

/// <summary>Reports the host's installed <c>dotnet</c>/<c>git</c> CLI versions, for the dashboard's Status page.</summary>
public interface IInstalledToolVersionsProvider
{
    /// <summary>Returns the installed tool versions - computed once and cached for the service's lifetime, since they don't change without a restart.</summary>
    Task<InstalledToolVersions> GetVersionsAsync(CancellationToken cancellationToken);
}
