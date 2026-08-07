using AsciiFlow.Core.Encoding;

namespace AsciiFlow.Core.Tests;

public class MediaOutputProfileTests
{
    [Theory]
    [InlineData("output.MP4", "mp4", "libx264")]
    [InlineData("output.mkv", "matroska", "libx264")]
    [InlineData("output.m4v", "mp4", "libx264")]
    [InlineData("output.mov", "mov", "libx264")]
    [InlineData("output.avi", "avi", "libx264")]
    [InlineData("output.ts", "mpegts", "libx264")]
    [InlineData("output.m2ts", "mpegts", "libx264")]
    [InlineData("output.webm", "webm", "libvpx-vp9")]
    public void FromPath_SelectsContainerAndCodec(
        string path,
        string expectedContainer,
        string expectedCodec)
    {
        MediaOutputProfile profile = MediaOutputProfile.FromPath(path);

        Assert.Equal(expectedContainer, profile.ContainerFormat);
        Assert.Equal(expectedCodec, profile.VideoCodecName);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("output.gif")]
    [InlineData("output.flv")]
    [InlineData("output.txt")]
    public void FromPath_RejectsUnsupportedOutputFormat(string path)
    {
        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => MediaOutputProfile.FromPath(path));

        Assert.Contains(".mp4", error.Message);
        Assert.Contains(".webm", error.Message);
    }

    [Fact]
    public void FormatEncoderSettings_UsesVp9OptionsForWebM()
    {
        MediaOutputProfile profile = MediaOutputProfile.FromPath("output.webm");

        string result = profile.FormatEncoderSettings(VideoEncoderSettings.Speed);

        Assert.Equal("realtime · cpu-used 8 · CRF 20", result);
    }

    [Fact]
    public void FormatEncoderSettings_ShowsZeroBFramesForH264SpeedMode()
    {
        MediaOutputProfile profile = MediaOutputProfile.FromPath("output.mp4");

        string result = profile.FormatEncoderSettings(VideoEncoderSettings.Speed);

        Assert.Equal("ultrafast · CRF 20 · 0 B 帧", result);
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(0, false, false)]
    [InlineData(-1, false, false)]
    [InlineData(0, true, true)]
    [InlineData(-1, true, true)]
    public void AudioStreamCopyRequiresConfirmedSupportOrKnownContainerException(
        int compatibility,
        bool knownContainerCodecException,
        bool expected)
    {
        Assert.Equal(
            expected,
            FFmpegVideoEncoder.ShouldAttachAudioStream(
                compatibility,
                knownContainerCodecException));
    }
}
