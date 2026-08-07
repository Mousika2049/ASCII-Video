using AsciiFlow.Core.Encoding;

namespace AsciiFlow.Core.Tests;

public class Vp9EncoderTuningTests
{
    [Fact]
    public void SpeedModeUsesParallelLowLatencySettingsAt720p()
    {
        Vp9EncoderTuning tuning = Vp9EncoderTuning.Create(
            VideoEncoderSettings.Speed,
            1280,
            720,
            22);

        Assert.Equal("realtime", tuning.Deadline);
        Assert.Equal(8, tuning.CpuUsed);
        Assert.Equal(8, tuning.ThreadCount);
        Assert.Equal(2, tuning.TileColumns);
        Assert.Equal(4, tuning.TileCount);
        Assert.Equal(0, tuning.LagInFrames);
    }

    [Fact]
    public void BalancedModePreservesLookaheadAndRespectsAvailableProcessors()
    {
        Vp9EncoderTuning tuning = Vp9EncoderTuning.Create(
            VideoEncoderSettings.Balanced,
            1920,
            1080,
            4);

        Assert.Equal("good", tuning.Deadline);
        Assert.Equal(6, tuning.CpuUsed);
        Assert.Equal(4, tuning.ThreadCount);
        Assert.Equal(2, tuning.TileColumns);
        Assert.Null(tuning.LagInFrames);
    }

    [Fact]
    public void SmallFramesDoNotCreateMoreTilesThanThreadsCanUse()
    {
        Vp9EncoderTuning tuning = Vp9EncoderTuning.Create(
            VideoEncoderSettings.Speed,
            320,
            180,
            1);

        Assert.Equal(1, tuning.ThreadCount);
        Assert.Equal(0, tuning.TileColumns);
    }

    [Theory]
    [InlineData(0, 720, 8)]
    [InlineData(1280, 0, 8)]
    [InlineData(1280, 720, 0)]
    public void InvalidRuntimeDimensionsAreRejected(int width, int height, int processorCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Vp9EncoderTuning.Create(
                VideoEncoderSettings.Speed,
                width,
                height,
                processorCount));
    }
}
