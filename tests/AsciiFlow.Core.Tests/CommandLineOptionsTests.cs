using AsciiFlow.App;

namespace AsciiFlow.Core.Tests;

public class CommandLineOptionsTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void Color_ParsesExplicitBooleanValue(string input, bool expected)
    {
        var options = new CommandLineOptions { ColorValue = input };

        Assert.Equal(expected, options.Color);
    }

    [Fact]
    public void Color_RejectsUnexpectedValue()
    {
        var options = new CommandLineOptions { ColorValue = "sometimes" };

        Assert.Throws<ArgumentException>(() => options.Color);
    }
}
