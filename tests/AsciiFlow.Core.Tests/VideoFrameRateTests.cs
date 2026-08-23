using AsciiFlow.Core.Video;

namespace AsciiFlow.Core.Tests;

public class VideoFrameRateTests
{
    [Theory]
    [InlineData(29.97, 2997, 100)]
    [InlineData(23.976, 2997, 125)]
    [InlineData(60, 60, 1)]
    public void FromDouble_PreservesExpectedRatio(double value, int numerator, int denominator)
    {
        VideoFrameRate result = VideoFrameRate.FromDouble(value);

        Assert.Equal(new VideoFrameRate(numerator, denominator), result);
    }

    [Fact]
    public void Reduce_NormalizesRatio()
    {
        Assert.Equal(new VideoFrameRate(30000, 1001), new VideoFrameRate(60000, 2002).Reduce());
    }

    [Fact]
    public void VideoInfo_EstimatesFrameCountWithoutOverwritingAuthoritativeCount()
    {
        var info = new VideoInfo(
            3840,
            2160,
            new VideoFrameRate(24, 1),
            frameCount: 0,
            durationSeconds: 216.402);

        Assert.Equal(0, info.FrameCount);
        Assert.Equal(5194, info.EstimatedFrameCount);
        Assert.Equal(216.402, info.DurationSeconds);
    }

    [Fact]
    public void VideoInfo_DoesNotEstimateWhenAuthoritativeFrameCountExists()
    {
        var info = new VideoInfo(
            1920,
            1080,
            new VideoFrameRate(30, 1),
            frameCount: 300,
            durationSeconds: 10.1);

        Assert.Equal(300, info.FrameCount);
        Assert.Null(info.EstimatedFrameCount);
    }

    [Fact]
    public void VideoInfo_LeavesFrameCountUnknownWithoutDuration()
    {
        var info = new VideoInfo(1920, 1080, new VideoFrameRate(30, 1), frameCount: 0);

        Assert.Equal(0, info.FrameCount);
        Assert.Null(info.EstimatedFrameCount);
    }
}
