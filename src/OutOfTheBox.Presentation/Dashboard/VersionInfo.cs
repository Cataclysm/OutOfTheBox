// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Reflection;

namespace OutOfTheBox.Presentation.Dashboard;

/// <summary>
/// The single place that reads the running build's version via reflection, per design.md's
/// "Dashboard branding" decision - both <see cref="VersionEndpoints"/> and the shared layout's
/// branding element read the same value through here, rather than each doing its own reflection call.
/// </summary>
public static class VersionInfo
{
    /// <summary>The running build's informational version (from <c>Directory.Build.props</c>'s <c>&lt;Version&gt;</c>), or <c>"unknown"</c> if unavailable.</summary>
    public static string Current { get; } =
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
}
