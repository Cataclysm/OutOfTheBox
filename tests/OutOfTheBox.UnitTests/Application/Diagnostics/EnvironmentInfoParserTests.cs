// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Application.Diagnostics;

namespace OutOfTheBox.UnitTests.Application.Diagnostics;

public sealed class EnvironmentInfoParserTests
{
    [Fact]
    public void ParseSdkList_returns_empty_for_null_or_empty_output() => Assert.Empty(EnvironmentInfoParser.ParseSdkList(null));

    [Fact]
    public void ParseSdkList_parses_a_single_sdk()
    {
        var sdks = EnvironmentInfoParser.ParseSdkList("10.0.302 [C:\\Program Files\\dotnet\\sdk]\n");

        var sdk = Assert.Single(sdks);
        Assert.Equal("10.0.302", sdk.Version);
        Assert.Equal("C:\\Program Files\\dotnet\\sdk", sdk.Path);
    }

    [Fact]
    public void ParseSdkList_parses_multiple_sdks()
    {
        var output = "8.0.100 [C:\\Program Files\\dotnet\\sdk]\n10.0.302 [C:\\Program Files\\dotnet\\sdk]\n";

        var sdks = EnvironmentInfoParser.ParseSdkList(output);

        Assert.Equal(2, sdks.Count);
        Assert.Equal("8.0.100", sdks[0].Version);
        Assert.Equal("10.0.302", sdks[1].Version);
    }

    [Fact]
    public void ParseSdkList_ignores_unrecognized_lines()
    {
        var sdks = EnvironmentInfoParser.ParseSdkList("some unrelated warning line\n10.0.302 [C:\\sdk]\n");

        var sdk = Assert.Single(sdks);
        Assert.Equal("10.0.302", sdk.Version);
    }

    [Fact]
    public void ParseNuGetSourceList_returns_empty_for_null_or_empty_output() => Assert.Empty(EnvironmentInfoParser.ParseNuGetSourceList(null));

    [Fact]
    public void ParseNuGetSourceList_parses_a_single_enabled_source()
    {
        var output = "Registered Sources:\n  1.  nuget.org [Enabled]\n      https://api.nuget.org/v3/index.json\n";

        var sources = EnvironmentInfoParser.ParseNuGetSourceList(output);

        var source = Assert.Single(sources);
        Assert.Equal("nuget.org", source.Name);
        Assert.Equal("https://api.nuget.org/v3/index.json", source.Url);
        Assert.True(source.IsEnabled);
    }

    [Fact]
    public void ParseNuGetSourceList_parses_multiple_sources_including_a_disabled_one()
    {
        var output =
            "Registered Sources:\n" +
            "  1.  nuget.org [Enabled]\n" +
            "      https://api.nuget.org/v3/index.json\n" +
            "  2.  Contoso [Disabled]\n" +
            "      https://contoso.example/nuget\n";

        var sources = EnvironmentInfoParser.ParseNuGetSourceList(output);

        Assert.Equal(2, sources.Count);
        Assert.True(sources[0].IsEnabled);
        Assert.Equal("Contoso", sources[1].Name);
        Assert.False(sources[1].IsEnabled);
        Assert.Equal("https://contoso.example/nuget", sources[1].Url);
    }

    [Fact]
    public void ParseWorkloadList_returns_empty_when_none_installed()
    {
        // Real `dotnet workload list` output when nothing is installed - a sentence, not a table.
        var output =
            "\nWorkload version: 10.0.300-manifests.1641d827\n\n" +
            "Installed Workload Id      Manifest Version      Installation Source\n" +
            "--------------------------------------------------------------------\n\n" +
            "Use `dotnet workload search` to find additional workloads to install.\n";

        Assert.Empty(EnvironmentInfoParser.ParseWorkloadList(output));
    }

    [Fact]
    public void ParseWorkloadList_extracts_workload_ids_from_data_rows()
    {
        var output =
            "\nWorkload version: 10.0.300-manifests.1641d827\n\n" +
            "Installed Workload Id      Manifest Version      Installation Source\n" +
            "--------------------------------------------------------------------\n" +
            "wasm-tools                 10.0.0/10.0.100        SDK 10.0.300\n" +
            "maui-android                10.0.0/10.0.100        SDK 10.0.300\n\n" +
            "Use `dotnet workload search` to find additional workloads to install.\n";

        var ids = EnvironmentInfoParser.ParseWorkloadList(output);

        Assert.Equal(["wasm-tools", "maui-android"], ids);
    }

    [Fact]
    public void ParseWorkloadList_returns_empty_for_null_or_empty_output() => Assert.Empty(EnvironmentInfoParser.ParseWorkloadList(null));
}
