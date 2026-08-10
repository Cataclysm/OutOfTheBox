// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using OutOfTheBox.Application.Execution;
using Microsoft.Extensions.Logging;

namespace OutOfTheBox.Infrastructure.Execution;

/// <inheritdoc cref="IInstalledToolVersionsProvider" />
/// <remarks>
/// Deliberately spawns <c>dotnet --version</c>/<c>git --version</c> directly via
/// <see cref="Process"/> rather than through <see cref="IProcessRunner"/> - that abstraction is
/// built for a caller-facing, run-tracked, per-repository-locked command execution (buffered output,
/// cancellation, resource sampling), none of which applies to this internal one-off version probe.
/// Registered as a singleton and caches its result for the service's lifetime (see
/// <see cref="IInstalledToolVersionsProvider.GetVersionsAsync"/>'s own doc comment) via
/// <see cref="Lazy{T}"/>, the standard thread-safe pattern for a cached async value computed at
/// most once.
/// </remarks>
public sealed class InstalledToolVersionsProvider(ILogger<InstalledToolVersionsProvider> logger) : IInstalledToolVersionsProvider
{
    private readonly Lazy<Task<InstalledToolVersions>> _cached = new(ComputeAsyncFactory(logger));

    /// <inheritdoc />
    public Task<InstalledToolVersions> GetVersionsAsync(CancellationToken cancellationToken) => _cached.Value;

    private static Func<Task<InstalledToolVersions>> ComputeAsyncFactory(ILogger<InstalledToolVersionsProvider> logger) => async () =>
    {
        var dotnetVersion = await RunVersionCommandAsync("dotnet", "--version", logger);
        var gitVersion = await RunVersionCommandAsync("git", "--version", logger);

        return new InstalledToolVersions(dotnetVersion, StripGitVersionPrefix(gitVersion));
    };

    // "git --version" prints "git version 2.43.0.windows.1" - stripped down to just "2.43.0.windows.1"
    // so it displays the same shape as dotnet's own version-only output ("10.0.100").
    private static string? StripGitVersionPrefix(string? rawOutput)
    {
        const string prefix = "git version ";
        return rawOutput is not null && rawOutput.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? rawOutput[prefix.Length..].Trim()
            : rawOutput;
    }

    private static async Task<string?> RunVersionCommandAsync(string executable, string argument, ILogger<InstalledToolVersionsProvider> logger)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Win32Exception ex)
        {
            // Not on PATH / not installed for *this process's* account - worth a trace since this
            // codebase has already chased a real bug where the tool genuinely was installed but
            // unreachable specifically for the service account's PATH/environment (see
            // GitRepositoryStatsProvider's own remarks on the same failure shape), which this
            // dashboard-facing version probe would otherwise report identically to "not installed at
            // all" with no way to tell the two apart.
            logger.LogWarning(ex, "{Executable} --version failed to start.", executable);
            return null;
        }
    }
}
