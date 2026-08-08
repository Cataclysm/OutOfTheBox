// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using Reqnroll;

namespace OutOfTheBox.BehaviorTests;

/// <summary>Step definitions backing <c>Sanity.feature</c> - a Reqnroll/dotnet-test toolchain check only.</summary>
[Binding]
public sealed class SanitySteps
{
    private int _a;
    private int _b;
    private int _result;

    [Given(@"I have the number (\d+)")]
    public void GivenIHaveTheNumber(int number)
    {
        if (_a == 0)
        {
            _a = number;
        }
        else
        {
            _b = number;
        }
    }

    [When(@"I add them together")]
    public void WhenIAddThemTogether() => _result = _a + _b;

    [Then(@"the result should be (\d+)")]
    public void ThenTheResultShouldBe(int expected) => Assert.Equal(expected, _result);
}
