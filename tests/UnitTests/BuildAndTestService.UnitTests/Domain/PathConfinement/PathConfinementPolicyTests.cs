using BuildAndTestService.Domain.PathConfinement;

namespace BuildAndTestService.UnitTests.Domain.PathConfinement;

public sealed class PathConfinementPolicyTests
{
    [Fact]
    public void IsContained_returns_true_for_the_root_itself()
    {
        Assert.True(PathConfinementPolicy.IsContained(@"C:\repos", @"C:\repos"));
    }

    [Fact]
    public void IsContained_returns_true_for_a_subdirectory()
    {
        Assert.True(PathConfinementPolicy.IsContained(@"C:\repos", @"C:\repos\myrepo\src"));
    }

    [Fact]
    public void IsContained_returns_false_for_a_sibling_directory_sharing_a_name_prefix()
    {
        // The classic naive-StartsWith bug: "C:\repos-evil" starts with the string "C:\repos"
        // but is not actually inside it.
        Assert.False(PathConfinementPolicy.IsContained(@"C:\repos", @"C:\repos-evil"));
    }

    [Fact]
    public void IsContained_returns_false_for_an_unrelated_path()
    {
        Assert.False(PathConfinementPolicy.IsContained(@"C:\repos", @"C:\Windows\System32"));
    }

    [Fact]
    public void IsContained_returns_false_for_the_parent_of_the_root()
    {
        Assert.False(PathConfinementPolicy.IsContained(@"C:\repos\myrepo", @"C:\repos"));
    }

    [Fact]
    public void IsContained_is_case_insensitive()
    {
        Assert.True(PathConfinementPolicy.IsContained(@"C:\repos", @"C:\REPOS\myrepo"));
    }

    [Fact]
    public void IsContained_ignores_a_trailing_separator_on_the_root()
    {
        Assert.True(PathConfinementPolicy.IsContained(@"C:\repos\", @"C:\repos\myrepo"));
    }
}
