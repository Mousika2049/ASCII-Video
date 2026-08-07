using AsciiFlow.App.Core;
using AsciiFlow.Core.AsciiMapping;
using AsciiFlow.Core.Encoding;
using AsciiFlow.Core.Rendering;
using AsciiFlow.Core.Video;

namespace AsciiFlow.Core.Tests;

public class VideoPipelineConcurrencyTests
{
    [Fact]
    public async Task Process_OverlapsStagesWithinThreeReusableSlots_AndPreservesOrder()
    {
        const int frameCount = 6;
        var decoder = new SequenceDecoder(frameCount);
        var encoder = new BlockingEncoder();
        using var pipeline = new VideoPipeline(
            () => decoder,
            _ => new SequenceMapper(),
            (_, _, _, _, _) => new SequenceRenderer(),
            () => encoder);
        var request = CreateRequest();
        pipeline.Initialize(request);

        Task<int> processing = Task.Run(() => pipeline.Process(request));
        try
        {
            Assert.True(encoder.FirstFrameStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(
                SpinWait.SpinUntil(
                    () => decoder.VideoFramesReturned == VideoPipeline.PipelineBufferCount,
                    TimeSpan.FromSeconds(5)),
                $"编码阻塞时仅解码了 {decoder.VideoFramesReturned} 帧");
            Assert.Equal(VideoPipeline.PipelineBufferCount, decoder.VideoFramesReturned);
        }
        finally
        {
            encoder.AllowFirstFrameToFinish.Set();
        }

        int processed = await processing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(frameCount, processed);
        Assert.Equal(Enumerable.Range(0, frameCount).Select(value => (byte)value), encoder.FrameMarkers);
        Assert.False(encoder.RgbPathUsed);
    }

    private static VideoProcessingRequest CreateRequest() => new()
    {
        InputFile = Path.Combine(Path.GetTempPath(), $"asciiflow-input-{Guid.NewGuid():N}.mp4"),
        OutputFile = Path.Combine(Path.GetTempPath(), $"asciiflow-output-{Guid.NewGuid():N}.mp4"),
        Width = 2,
        Height = 2,
        CharSet = "standard",
        FontSize = 2,
        FontFamily = "monospace",
        EncoderMode = "speed",
        NoProgress = true
    };

    private sealed class SequenceDecoder(int frameCount) : IVideoDecoder
    {
        private int _nextFrame;
        private int _videoFramesReturned;

        public int Width => 2;
        public int Height => 2;
        public double FrameRate => 30;
        public long FrameCount => frameCount;
        public long CurrentFrame => Volatile.Read(ref _videoFramesReturned);
        public bool IsInitialized { get; private set; }
        public int VideoFramesReturned => Volatile.Read(ref _videoFramesReturned);

        public void Initialize(string videoPath) => IsInitialized = true;

        public byte[]? GetNextFrame()
        {
            int marker = Interlocked.Increment(ref _nextFrame) - 1;
            if (marker >= frameCount)
                return null;

            Interlocked.Increment(ref _videoFramesReturned);
            return Enumerable.Repeat((byte)marker, Width * Height * 3).ToArray();
        }

        public void SeekToFrame(long frameNumber) => throw new NotSupportedException();

        public VideoInfo GetVideoInfo() =>
            new(Width, Height, new VideoFrameRate(30, 1), frameCount);

        public void Reset() => _nextFrame = 0;
        public void Dispose() { }
    }

    private sealed class SequenceMapper : IAsciiMapper
    {
        public AsciiFrame MapRgb(
            byte[] rgbData,
            int width,
            int height,
            int targetWidth,
            int targetHeight,
            bool includeColor) =>
            new(targetWidth, targetHeight, [(char)rgbData[0], '\0', '\0', '\0'], null);

        public AsciiFrame Map(
            byte[] grayData,
            int width,
            int height,
            int targetWidth,
            int targetHeight,
            byte[]? rgbData = null) =>
            throw new NotSupportedException();

        public string MapToAscii(
            byte[] grayData,
            int width,
            int height,
            int targetWidth,
            int targetHeight) =>
            throw new NotSupportedException();
    }

    private sealed class SequenceRenderer : IAsciiRenderer, IYuv420pAsciiRenderer
    {
        public int OutputWidth => 2;
        public int OutputHeight => 2;
        public int CharWidth => 1;
        public int CharHeight => 1;

        public void Initialize() { }

        public byte[] RenderFrame(AsciiFrame frame, bool useColor = true) =>
            throw new InvalidOperationException("流水线不应生成 RGB 渲染帧");

        public void RenderFrameYuv420p(AsciiFrame frame, byte[] destination, bool useColor = true) =>
            Array.Fill(destination, (byte)frame.Characters[0]);

        public byte[] RenderFrame(string asciiArt) => throw new NotSupportedException();

        public byte[] RenderFrameWithColor(
            string asciiArt,
            (byte R, byte G, byte B)[] colors,
            bool useColor = true) =>
            throw new NotSupportedException();

        public void Dispose() { }
    }

    private sealed class BlockingEncoder : IVideoEncoder, IYuv420pVideoEncoder
    {
        private readonly List<byte> _frameMarkers = [];
        private readonly object _sync = new();

        public ManualResetEventSlim FirstFrameStarted { get; } = new(false);
        public ManualResetEventSlim AllowFirstFrameToFinish { get; } = new(false);
        public IReadOnlyList<byte> FrameMarkers
        {
            get
            {
                lock (_sync)
                    return _frameMarkers.ToArray();
            }
        }

        public bool IsInitialized { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public double FrameRate { get; private set; }
        public long EncodedFrames { get; private set; }
        public bool RgbPathUsed { get; private set; }

        public void Initialize(string outputPath, int width, int height, VideoFrameRate frameRate)
        {
            Width = width;
            Height = height;
            FrameRate = frameRate.Value;
            IsInitialized = true;
        }

        public void EncodeFrame(byte[] rgbData)
        {
            RgbPathUsed = true;
            Encode(rgbData);
        }

        public void EncodeYuv420pFrame(byte[] yuvData) => Encode(yuvData);

        private void Encode(byte[] frameData)
        {
            if (EncodedFrames == 0)
            {
                FirstFrameStarted.Set();
                if (!AllowFirstFrameToFinish.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("等待测试释放首帧编码超时");
            }

            lock (_sync)
                _frameMarkers.Add(frameData[0]);
            EncodedFrames++;
        }

        public void Finish() => IsInitialized = false;
        public void Dispose() { }
    }
}
