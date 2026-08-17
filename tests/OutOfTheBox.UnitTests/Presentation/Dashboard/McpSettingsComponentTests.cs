// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using OutOfTheBox.Application.Mcp;
using OutOfTheBox.Domain.Mcp;
using OutOfTheBox.Presentation.Dashboard;
using AngleSharp.Html.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>Renders the real <see cref="McpSettings"/> component via bUnit against a fake <see cref="IMcpPermissionStore"/> - never a real database, matching this project's other dashboard-component test conventions.</summary>
public sealed class McpSettingsComponentTests : DashboardComponentTestContext
{
    private readonly FakeMcpPermissionStore _permissionStore = new();

    public McpSettingsComponentTests() => Services.AddSingleton<IMcpPermissionStore>(_permissionStore);

    [Fact]
    public void Renders_every_group_and_catalog_entry()
    {
        var cut = Render<McpSettings>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("dotnet subcommands", cut.Markup);
            Assert.Contains("git subcommands", cut.Markup);
            Assert.Contains("Run management", cut.Markup);
            Assert.Contains("File management", cut.Markup);
            Assert.Contains("publish", cut.Markup); // a newly-catalogued, default-disabled dotnet subcommand
            Assert.Contains("push", cut.Markup); // a newly-catalogued, default-disabled git subcommand
            Assert.Contains("find_files", cut.Markup);
            Assert.Contains("authorize_git_host", cut.Markup);
        });
    }

    [Fact]
    public void A_default_enabled_plain_tool_checkbox_starts_checked_and_disabling_it_persists()
    {
        var cut = Render<McpSettings>();
        cut.WaitForAssertion(() => Assert.Contains("find_files", cut.Markup));

        var checkbox = FindCheckboxFor(cut, "find_files");
        Assert.True(checkbox.IsChecked);

        checkbox.Change(false);

        Assert.Contains(("find_files", false), _permissionStore.SetCalls);
        Assert.False(FindCheckboxFor(cut, "find_files").IsChecked);
    }

    [Fact]
    public void A_default_disabled_subcommand_checkbox_starts_unchecked_and_enabling_it_persists_the_subcommand_key()
    {
        var cut = Render<McpSettings>();
        cut.WaitForAssertion(() => Assert.Contains("publish", cut.Markup));

        var checkbox = FindCheckboxFor(cut, "publish");
        Assert.False(checkbox.IsChecked);

        checkbox.Change(true);

        Assert.Contains(("dotnet:publish", true), _permissionStore.SetCalls);
    }

    private static IHtmlInputElement FindCheckboxFor(IRenderedComponent<McpSettings> cut, string labelText) =>
        (IHtmlInputElement)cut.FindAll("label")
            .Single(label => label.TextContent.Trim() == labelText)
            .QuerySelector("input[type=checkbox]")!;

    private sealed class FakeMcpPermissionStore : IMcpPermissionStore
    {
        private readonly Dictionary<string, bool> _state = McpToolCatalog.AllKeys().ToDictionary(key => key, McpToolCatalog.DefaultEnabled, StringComparer.Ordinal);

        public List<(string Key, bool Enabled)> SetCalls { get; } = [];

        public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool IsEnabled(string key) => _state.TryGetValue(key, out var enabled) && enabled;

        public Task<IReadOnlyDictionary<string, bool>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>(_state, StringComparer.Ordinal));

        public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken)
        {
            SetCalls.Add((key, enabled));
            _state[key] = enabled;
            return Task.CompletedTask;
        }
    }
}
