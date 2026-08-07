namespace HangingFixture;

public class SampleTests
{
    /// <summary>
    /// Deliberately never returns - simulates a hung test/testhost.exe for exercising the
    /// service's timeout and cancellation paths.
    /// </summary>
    [Fact]
    public async Task Never_completes()
    {
        await Task.Delay(Timeout.Infinite);
    }
}
