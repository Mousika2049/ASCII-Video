using AsciiFlow.App;

namespace AsciiFlow.Core.Tests;

public class TerminalDisplayTests
{
    [Theory]
    [InlineData(240L, null, "240 帧")]
    [InlineData(0L, 5194L, "帧数未知（按时长估算约 5194 帧）")]
    [InlineData(0L, null, "帧数未知")]
    public void SourceFrameCountDistinguishesAuthoritativeEstimatedAndUnknownTotals(
        long frameCount,
        long? estimatedFrameCount,
        string expected)
    {
        Assert.Equal(expected, TerminalDisplay.FormatSourceFrameCount(frameCount, estimatedFrameCount));
    }

    [Fact]
    public void EstimatedProgressIsMarkedAndDoesNotClaimCompletion()
    {
        string result = TerminalDisplay.FormatProgress(
            processedFrames: 120,
            totalFrames: 100,
            totalIsEstimated: true,
            framesPerSecond: 48,
            barWidth: 10);

        Assert.Equal("处理    [█████████░] 约 99.9% · 120/约 100 帧 · 48 FPS", result);
    }

    [Fact]
    public void UnknownProgressKeepsFrameCountUseful()
    {
        string result = TerminalDisplay.FormatProgress(
            processedFrames: 12,
            totalFrames: null,
            totalIsEstimated: false,
            framesPerSecond: 6,
            barWidth: 10);

        Assert.Equal("处理    [░░░░░░░░░░]   0.0% · 12/? 帧 · 6 FPS", result);
    }

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
