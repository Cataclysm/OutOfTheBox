// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>
// Licensed under the GNU Affero General Public License v3.0 or later - see LICENSE in the project
// root, or <https://www.gnu.org/licenses/agpl-3.0.html>, for the full text.

using OutOfTheBox.Domain.PathConfinement;

namespace OutOfTheBox.UnitTests.Domain.PathConfinement;

public sealed class PathConfinementPolicyTests
{
    [Fact]
    public void IsContained_returns_true_for_the_root_itself() => Assert.True(PathConfinementPolicy.IsContained(@"C:\repositories", @"C:\repositories"));

    [Fact]
    public void IsContained_returns_true_for_a_subdirectory() => Assert.True(PathConfinementPolicy.IsContained(@"C:\repositories", @"C:\repositories\myrepo\src"));

    [Fact]
    public void IsContained_returns_false_for_a_sibling_directory_sharing_a_name_prefix() =>
        // The classic naive-StartsWith bug: "C:\repositories-evil" starts with the string "C:\repositories"
        // but is not actually inside it.
        Assert.False(PathConfinementPolicy.IsContained(@"C:\repositories", @"C:\repositories-evil"));

    [Fact]
    public void IsContained_returns_false_for_an_unrelated_path() => Assert.False(PathConfinementPolicy.IsContained(@"C:\repositories", @"C:\Windows\System32"));

    [Fact]
    public void IsContained_returns_false_for_the_parent_of_the_root() => Assert.False(PathConfinementPolicy.IsContained(@"C:\repositories\myrepo", @"C:\repositories"));

    [Fact]
    public void IsContained_is_case_insensitive() => Assert.True(PathConfinementPolicy.IsContained(@"C:\repositories", @"C:\REPOSITORIES\myrepo"));

    [Fact]
    public void IsContained_ignores_a_trailing_separator_on_the_root() => Assert.True(PathConfinementPolicy.IsContained(@"C:\repositories\", @"C:\repositories\myrepo"));
}
