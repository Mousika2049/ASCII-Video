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
}
