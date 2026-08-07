namespace FailingFixture;

public class SampleTests
{
    [Fact]
    public void Addition_is_deliberately_wrong()
    {
        Assert.Equal(5, 2 + 2);
    }
}
