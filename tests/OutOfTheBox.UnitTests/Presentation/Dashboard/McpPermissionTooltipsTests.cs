// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.Mcp;
using OutOfTheBox.Presentation.Dashboard;

namespace OutOfTheBox.UnitTests.Presentation.Dashboard;

/// <summary>
/// Guards <see cref="McpPermissionTooltips"/> against drifting out of sync with
/// <see cref="McpToolCatalog"/> - the two are maintained by hand in separate files (Domain doesn't
/// know about this Presentation-only display copy), so nothing but this test catches a catalog key
/// added without a matching tooltip, or a tooltip left behind for a key the catalog no longer has.
/// </summary>
public sealed class McpPermissionTooltipsTests
{
    [Fact]
    public void Tooltip_keys_exactly_match_the_catalog()
    {
        var catalogKeys = McpToolCatalog.AllKeys().ToHashSet(StringComparer.Ordinal);
        var tooltipKeys = McpPermissionTooltips.Keys.ToHashSet(StringComparer.Ordinal);

        Assert.True(catalogKeys.SetEquals(tooltipKeys));
    }

    [Theory]
    [MemberData(nameof(AllCatalogKeys))]
    public void Every_tooltip_has_non_empty_what_how_and_risk_text(string key)
    {
        var tip = McpPermissionTooltips.For(key);

        Assert.False(string.IsNullOrWhiteSpace(tip.What));
        Assert.False(string.IsNullOrWhiteSpace(tip.How));
        Assert.False(string.IsNullOrWhiteSpace(tip.Risk));
    }

    public static IEnumerable<object[]> AllCatalogKeys() => McpToolCatalog.AllKeys().Select(key => new object[] { key });
}
