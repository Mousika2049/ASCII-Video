using AsciiFlow.App;

namespace AsciiFlow.Core.Tests;

public class TerminalDisplayTests
{
    [Theory]
    [InlineData(1024, "1.0 KB")]
    [InlineData(10 * 1024 * 1024, "10.00 MB")]
    public void FileSizeUsesCompactBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, TerminalDisplay.FormatFileSize(bytes));
    }

    [Fact]
    public void CompletionContainsOnlyEssentialSummaryValues()
    {
        string result = TerminalDisplay.FormatCompletion(120, 1.5, 10 * 1024 * 1024);

        Assert.Equal("120 帧 · 1.50s · 80.0 FPS · 10.00 MB", result);
    }
}
