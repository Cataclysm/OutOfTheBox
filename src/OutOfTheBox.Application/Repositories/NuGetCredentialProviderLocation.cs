// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

namespace OutOfTheBox.Application.Repositories;

/// <summary>
/// The fixed, service-controlled install location for the bundled Azure Artifacts Credential
/// Provider - a plain string constant, not a port, since both <c>NuGetFeedCredentialStore</c>
/// (Infrastructure, which checks it's actually present before accepting an Azure DevOps Artifacts
/// authorization) and <c>CommandExecutionMcpTools</c> (Presentation, which points
/// <c>NUGET_NETCORE_PLUGIN_PATHS</c> at it on every <c>dotnet_run</c> spawn) need the identical
/// value, and Presentation cannot reference Infrastructure - see design.md's "installer bundling"
/// decision. Deliberately not a per-user <c>~/.nuget/plugins</c> location, since this service runs
/// under a dedicated least-privilege account whose profile may not be loaded.
/// </summary>
public static class NuGetCredentialProviderLocation
{
    /// <summary>Where this service's installer extracts the vendored Azure Artifacts Credential Provider release archive, under this process's own install directory.</summary>
    public static string InstallDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "CredentialProviders", "AzureArtifacts");

    /// <summary>
    /// The directory containing <c>CredentialProvider.Microsoft.dll</c> - <c>plugins\netcore\CredentialProvider.Microsoft\</c>
    /// under <see cref="InstallDirectory"/>, the nested layout the vendored
    /// microsoft/artifacts-credprovider release archive itself uses (confirmed against the real
    /// v2.0.4 <c>Microsoft.win-x64.NuGet.CredentialProvider.zip</c>), preserved as-is by the
    /// installer's file harvesting rather than flattened - a fixed constant of that specific
    /// vendored structure, not a guess.
    /// </summary>
    public static string PluginDirectory { get; } = Path.Combine(InstallDirectory, "plugins", "netcore", "CredentialProvider.Microsoft");

    /// <summary>
    /// The exact file <c>NUGET_NETCORE_PLUGIN_PATHS</c> must point at (a semicolon-separated list of
    /// full plugin file paths, not a directory to scan - confirmed against the real NuGet.Client
    /// source, <c>PluginDiscoverer.cs</c>: when this environment variable is set, its value is split
    /// directly into file paths with no further directory-convention resolution applied, unlike the
    /// no-env-var default discovery path). NuGet's own convention-based discovery (what runs when no
    /// override is set at all) looks for <c>&lt;pluginDirectory&gt;\&lt;pluginDirectory-name&gt;.dll</c>
    /// specifically for a non-desktop/`dotnet` context (not the self-contained <c>.exe</c> apphost
    /// also present in the archive) - matching that exact convention here, rather than guessing,
    /// since <c>dotnet_run</c> always spawns <c>dotnet</c>, never desktop/Framework tooling.
    /// </summary>
    public static string PluginFilePath { get; } = Path.Combine(PluginDirectory, "CredentialProvider.Microsoft.dll");
}
