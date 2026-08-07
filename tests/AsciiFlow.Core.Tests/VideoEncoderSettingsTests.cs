using AsciiFlow.Core.Encoding;

namespace AsciiFlow.Core.Tests;

public class VideoEncoderSettingsTests
{
    [Fact]
    public void BalancedModeUsesBenchmarkedCompactPreset()
    {
        VideoEncoderSettings settings = VideoEncoderSettings.FromMode("BALANCED");

        Assert.Equal("superfast", settings.Preset);
        Assert.Equal(20, settings.Crf);
        Assert.Null(settings.Tune);
        Assert.Equal(2, settings.MaxBFrames);
    }

    [Fact]
    public void SpeedModeUsesBenchmarkedDefault()
    {
        VideoEncoderSettings settings = VideoEncoderSettings.FromMode("speed");

        Assert.Equal("ultrafast", settings.Preset);
        Assert.Equal(20, settings.Crf);
        Assert.Null(settings.Tune);
        Assert.Equal(0, settings.MaxBFrames);
    }

    [Theory]
    [InlineData("quality", "fast")]
    [InlineData("speed", "ultrafast")]
    public void NamedModesResolveExpectedPreset(string mode, string expectedPreset)
    {
        Assert.Equal(expectedPreset, VideoEncoderSettings.FromMode(mode).Preset);
    }

    [Fact]
    public void UnknownModeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => VideoEncoderSettings.FromMode("unknown"));
    }

    [Theory]
    [InlineData("speed", false, 0)]
    [InlineData("balanced", false, 2)]
    [InlineData("quality", false, 2)]
    [InlineData("balanced", true, 0)]
    public void EncoderResolvesExpectedMaximumBFrames(string mode, bool isVp9, int expected)
    {
        Assert.Equal(
            expected,
            FFmpegVideoEncoder.ResolveMaxBFrames(VideoEncoderSettings.FromMode(mode), isVp9));
    }
}
