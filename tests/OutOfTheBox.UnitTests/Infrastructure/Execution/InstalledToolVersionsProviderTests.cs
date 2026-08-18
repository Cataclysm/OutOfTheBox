// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Infrastructure.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutOfTheBox.UnitTests.Infrastructure.Execution;

/// <summary>
/// Exercises the real <see cref="InstalledToolVersionsProvider"/> - real <c>dotnet --version</c>/
/// <c>git --version</c> processes, not fakes, since both are guaranteed present in any environment
/// that can build and test this repository (the build itself requires the .NET SDK; git is how the
/// repository got here) - the same "real OS state, not mocked" reasoning
/// <see cref="OutOfTheBox.UnitTests.Infrastructure.Monitoring.HostResourceSamplerTests"/> already
/// applies to <c>PerformanceCounter</c>. Not a real child-process-spawning concern this project's
/// UnitTests convention warns against (see CliProcessRunnerTests) - a `--version` probe exits
/// immediately, nothing long-running or interactive.
/// </summary>
public sealed class InstalledToolVersionsProviderTests
{
    [Fact]
    public async Task GetVersionsAsync_reports_both_tools_plausible_versions()
    {
        var provider = new InstalledToolVersionsProvider(NullLogger<InstalledToolVersionsProvider>.Instance);

        var versions = await provider.GetVersionsAsync(CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(versions.DotnetVersion));
        Assert.False(string.IsNullOrWhiteSpace(versions.GitVersion));
        Assert.DoesNotContain("git version", versions.GitVersion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetVersionsAsync_caches_the_result_across_calls()
    {
        var provider = new InstalledToolVersionsProvider(NullLogger<InstalledToolVersionsProvider>.Instance);

        var first = await provider.GetVersionsAsync(CancellationToken.None);
        var second = await provider.GetVersionsAsync(CancellationToken.None);

        Assert.Same(first, second);
    }
}
