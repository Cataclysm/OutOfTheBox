// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// The fixed, service-controlled install location for the bundled Azure Artifacts Credential
/// Provider - a plain string constant, not a port, since both <c>NuGetFeedCredentialStore</c>
/// (Infrastructure, which checks it's actually present before accepting an Azure DevOps Artifacts
/// authorization) and <c>CommandExecutionMcpTools</c> (Presentation, which points
/// <c>NUGET_CREDENTIALPROVIDERS_PATH</c> at it on every <c>dotnet_run</c> spawn) need the identical
/// value, and Presentation cannot reference Infrastructure - see design.md's "installer bundling"
/// decision. Deliberately not a per-user <c>~/.nuget/plugins</c> location, since this service runs
/// under a dedicated least-privilege account whose profile may not be loaded.
/// </summary>
public static class NuGetCredentialProviderLocation
{
    /// <summary>Where this service's installer extracts the vendored Azure Artifacts Credential Provider release archive, under this process's own install directory.</summary>
    public static string InstallDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "CredentialProviders", "AzureArtifacts");

    /// <summary>
    /// The exact directory <c>NUGET_CREDENTIALPROVIDERS_PATH</c> must point at for NuGet's plugin
    /// discovery to find <c>CredentialProvider.Microsoft.exe</c> - <c>plugins\netcore\CredentialProvider.Microsoft\</c>
    /// under <see cref="InstallDirectory"/>, the nested layout the vendored
    /// microsoft/artifacts-credprovider release archive itself uses (confirmed against the real
    /// v2.0.4 <c>Microsoft.win-x64.NuGet.CredentialProvider.zip</c>), preserved as-is by the
    /// installer's file harvesting rather than flattened - a fixed constant of that specific
    /// vendored structure, not a guess.
    /// </summary>
    public static string PluginDirectory { get; } = Path.Combine(InstallDirectory, "plugins", "netcore", "CredentialProvider.Microsoft");
}
