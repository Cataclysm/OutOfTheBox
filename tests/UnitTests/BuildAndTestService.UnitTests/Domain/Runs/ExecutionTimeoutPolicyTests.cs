using BuildAndTestService.Domain.Runs;

namespace BuildAndTestService.UnitTests.Domain.Runs;

public sealed class ExecutionTimeoutPolicyTests
{
    private static readonly TimeSpan Default = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Maximum = TimeSpan.FromHours(1);

    [Fact]
    public void Resolve_uses_the_default_when_caller_supplies_nothing()
    {
        var result = ExecutionTimeoutPolicy.Resolve(null, Default, Maximum);

        Assert.Equal(Default, result);
    }

    [Fact]
    public void Resolve_honors_a_caller_supplied_value_shorter_than_the_default()
    {
        var callerSupplied = TimeSpan.FromMinutes(2);

        var result = ExecutionTimeoutPolicy.Resolve(callerSupplied, Default, Maximum);

        Assert.Equal(callerSupplied, result);
    }

    [Fact]
    public void Resolve_clamps_a_caller_supplied_value_longer_than_the_maximum()
    {
        var callerSupplied = TimeSpan.FromHours(5);

        var result = ExecutionTimeoutPolicy.Resolve(callerSupplied, Default, Maximum);

        Assert.Equal(Maximum, result);
    }

    [Fact]
    public void Resolve_clamps_the_default_itself_if_misconfigured_above_the_maximum()
    {
        var result = ExecutionTimeoutPolicy.Resolve(null, TimeSpan.FromHours(2), Maximum);

        Assert.Equal(Maximum, result);
    }
}
