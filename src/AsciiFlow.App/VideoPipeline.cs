using System.Diagnostics;
using AsciiFlow.Core.AsciiMapping;
using AsciiFlow.Core.Encoding;
using AsciiFlow.Core.Processing;
using AsciiFlow.Core.Rendering;
using AsciiFlow.Core.Video;

namespace AsciiFlow.App.Core;

/// <summary>
/// 视频处理流水线管理器
/// 整合：解码 → 灰度 → 映射 → 渲染 → 编码
/// </summary>
public class VideoPipeline : IDisposable
{
    private readonly Func<IVideoDecoder> _decoderFactory;
    private readonly Func<IGrayscaleConverter> _grayscaleConverterFactory;
    private readonly Func<string, IAsciiMapper> _asciiMapperFactory;
    private readonly Func<CharacterSetConfig, int, int, int, int, IAsciiRenderer> _rendererFactory;
    private readonly Func<IVideoEncoder> _encoderFactory;

    // 各模块实例
    private IVideoDecoder _decoder = null!;
    private IGrayscaleConverter _grayscaleConverter = null!;
    private IAsciiMapper _asciiMapper = null!;
    private IAsciiRenderer _renderer = null!;
    private IVideoEncoder _encoder = null!;

    // 配置信息
    private int _asciiWidth;      // ASCII 目标宽度（字符数，如 80）
    private int _asciiHeight;     // ASCII 目标高度（字符数，如 40）
    private int _videoWidth;      // 输出视频宽度（像素，= _renderer.OutputWidth，用于编码器）
    private int _videoHeight;     // 输出视频高度（像素，= _renderer.OutputHeight，用于编码器）
                                  // 注意：解码器实际宽高应该通过 _decoder.Width/Height 访问，不要用这里的变量

    // 性能统计（毫秒）
    private double _decodeTimeMs;
    private double _grayscaleTimeMs;
    private double _mappingTimeMs;
    private double _renderTimeMs;
    private double _encodeTimeMs;

    private string? _finalOutputPath;
    private string? _stagingOutputPath;
    private bool _outputCommitted;

    private bool _disposed = false;

    public VideoPipeline()
        : this(
            () => new FFmpegVideoDecoder(),
            () => new ParallelGrayscaleConverter(),
            characterSet => new LookupTableAsciiMapper(characterSet),
            (config, gridWidth, gridHeight, outputWidth, outputHeight) =>
                new SkiaCachedAsciiRenderer(config, gridWidth, gridHeight, outputWidth, outputHeight),
            () => new FFmpegVideoEncoder())
    {
    }

    public VideoPipeline(
        Func<IVideoDecoder> decoderFactory,
        Func<IGrayscaleConverter> grayscaleConverterFactory,
        Func<string, IAsciiMapper> asciiMapperFactory,
        Func<CharacterSetConfig, int, int, int, int, IAsciiRenderer> rendererFactory,
        Func<IVideoEncoder> encoderFactory)
    {
        _decoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory));
        _grayscaleConverterFactory = grayscaleConverterFactory ?? throw new ArgumentNullException(nameof(grayscaleConverterFactory));
        _asciiMapperFactory = asciiMapperFactory ?? throw new ArgumentNullException(nameof(asciiMapperFactory));
        _rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));
        _encoderFactory = encoderFactory ?? throw new ArgumentNullException(nameof(encoderFactory));
    }

    // ─────────────────────────────────────────
    // 初始化
    // ─────────────────────────────────────────

    public void Initialize(VideoProcessingRequest request)
    {
        ValidateRequest(request);

        string inputPath = Path.GetFullPath(request.InputFile);
        _finalOutputPath = Path.GetFullPath(request.OutputFile);
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(inputPath, _finalOutputPath, pathComparison))
            throw new ArgumentException("输入文件和输出文件不能是同一路径");

        string outputDirectory = Path.GetDirectoryName(_finalOutputPath)
            ?? throw new ArgumentException("无法确定输出目录", nameof(request.OutputFile));
        Directory.CreateDirectory(outputDirectory);
        string outputName = Path.GetFileNameWithoutExtension(_finalOutputPath);
        string outputExtension = Path.GetExtension(_finalOutputPath);
        _stagingOutputPath = Path.Combine(
            outputDirectory,
            $".{outputName}.{Guid.NewGuid():N}.tmp{outputExtension}");

        // 1. 初始化视频解码器
        _decoder = _decoderFactory();
        _decoder.Initialize(inputPath);
        if (_decoder is FFmpegVideoDecoder)
        {
            FFmpeg.AutoGen.ffmpeg.av_log_set_level(
                request.Verbose ? FFmpeg.AutoGen.ffmpeg.AV_LOG_INFO : FFmpeg.AutoGen.ffmpeg.AV_LOG_ERROR);
        }

        var videoInfo = _decoder.GetVideoInfo();
        int srcWidth = videoInfo.Width;
        int srcHeight = videoInfo.Height;
        Console.WriteLine($"✓ 视频解码器: {videoInfo.Resolution}, {videoInfo.FrameRate:F2}fps, {videoInfo.FrameCount} 帧");

        // 计算 ASCII 字符网格尺寸 (默认 240 宽度，高度自动匹配原视频比例，16:9 为 135)
        _asciiWidth = Math.Min(request.Width, srcWidth);
        if (_asciiWidth != request.Width)
            Console.WriteLine($"⚠ ASCII 宽度已从 {request.Width} 调整为源视频可支持的 {_asciiWidth}");
        if (request.Height > 0)
        {
            _asciiHeight = Math.Min(request.Height, srcHeight);
        }
        else
        {
            _asciiHeight = (int)Math.Max(1, Math.Round(_asciiWidth * ((double)srcHeight / srcWidth)));
        }
        if ((long)_asciiWidth * _asciiHeight > 4_000_000)
            throw new ArgumentException("ASCII 网格不能超过 400 万个单元格，请降低宽度或高度");

        // 2. 初始化并行灰度转换器
        _grayscaleConverter = _grayscaleConverterFactory();
        Console.WriteLine($"✓ 并行灰度转换器已就绪");

        // 3. 初始化 ASCII 字符映射器
        string charSet = request.CharSet.ToLowerInvariant() switch
        {
            "standard" => LookupTableAsciiMapper.Standard,
            "detailed" => LookupTableAsciiMapper.Detailed,
            _ => LookupTableAsciiMapper.Standard
        };
        _asciiMapper = _asciiMapperFactory(charSet);
        Console.WriteLine($"✓ ASCII 映射器已就绪（字符集: {request.CharSet}, {charSet.Length} 字符）");

        // 4. 初始化 SkiaSharp 渲染器（生成视频像素分辨率完全对齐原视频分辨率）
        int charW = (int)Math.Max(1, Math.Ceiling((double)srcWidth / _asciiWidth));
        int charH = (int)Math.Max(1, Math.Ceiling((double)srcHeight / _asciiHeight));
        float fontSize = request.FontSize > 0 ? request.FontSize : (float)Math.Max(1, charH * 0.95);

        var config = new CharacterSetConfig
        {
            FontFamily = request.FontFamily,
            FontSize = fontSize,
            CharWidth = charW,
            CharHeight = charH,
            BackgroundColor = (0, 0, 0),
            ForegroundColor = (255, 255, 255)
        };
        int outputWidth = srcWidth + (srcWidth & 1);
        int outputHeight = srcHeight + (srcHeight & 1);
        _renderer = _rendererFactory(config, _asciiWidth, _asciiHeight, outputWidth, outputHeight);
        _renderer.Initialize();

        // 编码器使用的视频尺寸（遵循原视频尺寸）
        _videoWidth = _renderer.OutputWidth;    // 遵循 srcWidth
        _videoHeight = _renderer.OutputHeight;  // 遵循 srcHeight
        Console.WriteLine($"✓ SkiaSharp 渲染器已就绪（输出尺寸对齐原视频: {_videoWidth}x{_videoHeight} 像素, 网格: {_asciiWidth}x{_asciiHeight} 字符）");

        VideoFrameRate outputFrameRate = request.FrameRate > 0
            ? VideoFrameRate.FromDouble(request.FrameRate)
            : videoInfo.ExactFrameRate;
        if (!outputFrameRate.IsValid) outputFrameRate = new VideoFrameRate(30, 1);

        // 5. 初始化 H.264 编码器并挂载原视频音频轨
        _encoder = _encoderFactory();

        if (_decoder is FFmpegVideoDecoder ffmpegDecoder && _encoder is FFmpegVideoEncoder ffmpegEncoder)
        {
            unsafe
            {
                var audioStream = ffmpegDecoder.GetAudioStream();
                ffmpegEncoder.Initialize(_stagingOutputPath, _videoWidth, _videoHeight, outputFrameRate, audioStream);

                ffmpegDecoder.OnAudioPacket = (packet, stream) =>
                {
                    ffmpegEncoder.WriteAudioPacket(packet, stream);
                };
            }
        }
        else
        {
            _encoder.Initialize(_stagingOutputPath, _videoWidth, _videoHeight, outputFrameRate);
        }

        Console.WriteLine($"✓ H.264 编码器已就绪（{_videoWidth}x{_videoHeight} @ {outputFrameRate.Value:F3}fps, {outputFrameRate}）");
    }

    // ─────────────────────────────────────────
    // 处理流水线
    // ─────────────────────────────────────────

    public int Process(VideoProcessingRequest request, CancellationToken cancellationToken = default)
    {
        if (_decoder == null)
            throw new InvalidOperationException("流水线未初始化，请先调用 Initialize()");

        int totalFrames = 0;
        int? maxFrames = request.MaxFrames > 0 ? request.MaxFrames : null;
        long? estimatedTotalFrames = maxFrames.HasValue
            ? maxFrames.Value
            : (_decoder.FrameCount > 0 ? _decoder.FrameCount : null);

        // ====== [修复] 缓存解码器实际尺寸 ======
        int srcWidth = _decoder.Width;   // 原始视频宽度（如 1280）
        int srcHeight = _decoder.Height; // 原始视频高度（如 720）
        Console.WriteLine($"[流水线] 解码器源尺寸: {srcWidth}x{srcHeight}");
        Console.WriteLine($"[流水线] 渲染器输出: {_renderer.OutputWidth}x{_renderer.OutputHeight}");
        Console.WriteLine($"[流水线] ASCII 目标: {_asciiWidth}x{_asciiHeight} 字符");
        Console.WriteLine();
        // ========================================

        var progressSw = Stopwatch.StartNew();
        int lastProgress = 0;

        Console.WriteLine();
        Console.WriteLine("【处理开始】");

        var overallSw = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (maxFrames.HasValue && totalFrames >= maxFrames.Value)
                break;

            // ① 解码一帧 (~30ms)
            var sw = Stopwatch.StartNew();
            byte[]? rgbFrame = _decoder.GetNextFrame();
            _decodeTimeMs += sw.Elapsed.TotalMilliseconds;

            if (rgbFrame == null)
                break; // 视频结束

            // ② RGB24 → Grayscale（并行）
            //    【修复】使用解码器的实际宽高 srcWidth/srcHeight
            sw.Restart();
            byte[] grayFrame = _grayscaleConverter.ConvertToGrayscale(rgbFrame, srcWidth, srcHeight);
            _grayscaleTimeMs += sw.Elapsed.TotalMilliseconds;

            // ③ Grayscale + RGB → ASCII string & Cell Colors
            sw.Restart();
            byte[] renderedFrame;
            AsciiFrame asciiFrame = _asciiMapper.Map(
                grayFrame,
                srcWidth,
                srcHeight,
                _asciiWidth,
                _asciiHeight,
                request.Color ? rgbFrame : null);
            _mappingTimeMs += sw.Elapsed.TotalMilliseconds;

            // ④ ASCII cells + Colors → RGB24 image
            sw.Restart();
            renderedFrame = _renderer.RenderFrame(asciiFrame, request.Color);
            _renderTimeMs += sw.Elapsed.TotalMilliseconds;

            // ⑤ RGB24 → H.264 packet (~20ms, libx264)
            sw.Restart();
            _encoder.EncodeFrame(renderedFrame);
            _encodeTimeMs += sw.Elapsed.TotalMilliseconds;

            totalFrames++;

            // 进度显示（每秒一次）
            if (!request.NoProgress && progressSw.ElapsedMilliseconds >= 1000)
            {
                double elapsedSeconds = Math.Max(0.001, progressSw.Elapsed.TotalSeconds);
                progressSw.Restart();
                int framesThisSec = totalFrames - lastProgress;
                lastProgress = totalFrames;

                double progress = estimatedTotalFrames > 0
                    ? Math.Min(100, (double)totalFrames / estimatedTotalFrames.Value * 100)
                    : 0;
                double fps = framesThisSec / elapsedSeconds;

                int barWidth = 30;
                int filled = (int)(progress / 100 * barWidth);
                string bar = new string('█', filled) + new string('░', barWidth - filled);

                Console.Write(
                    $"\r  ➤ [{bar}] {progress:F1}% | " +
                    $"帧: {totalFrames}/{(estimatedTotalFrames > 0 ? estimatedTotalFrames.ToString() : "?")} | " +
                    $"FPS: {fps:F0}");
            }
        }

        if (!request.NoProgress)
            Console.WriteLine();

        Console.WriteLine();
        Console.WriteLine($"【处理完成】共处理 {totalFrames} 帧 (耗时 {overallSw.Elapsed.TotalSeconds:F2}s)");

        if (totalFrames == 0)
            throw new InvalidOperationException("未从输入文件解码到任何视频帧");

        return totalFrames;
    }
    // ─────────────────────────────────────────
    // 完成编码
    // ─────────────────────────────────────────

    public void WriteTrailer()
    {
        if (_encoder != null && _encoder.IsInitialized)
        {
            _encoder.Finish();
        }

        if (string.IsNullOrEmpty(_stagingOutputPath) || string.IsNullOrEmpty(_finalOutputPath))
            throw new InvalidOperationException("输出路径尚未初始化");

        File.Move(_stagingOutputPath, _finalOutputPath, overwrite: true);
        _outputCommitted = true;
    }

    // ─────────────────────────────────────────
    // 性能统计
    // ─────────────────────────────────────────

    public PerformanceStats GetStatistics()
    {
        return new PerformanceStats
        {
            DecodeTimeMs = _decodeTimeMs,
            GrayscaleTimeMs = _grayscaleTimeMs,
            MappingTimeMs = _mappingTimeMs,
            RenderTimeMs = _renderTimeMs,
            EncodeTimeMs = _encodeTimeMs
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 注意：IGrayscaleConverter 没有 IDisposable，跳过
        try { (_decoder as IDisposable)?.Dispose(); } catch { }
        try { (_renderer as IDisposable)?.Dispose(); } catch { }
        try { (_encoder as IDisposable)?.Dispose(); } catch { }

        if (!_outputCommitted && !string.IsNullOrEmpty(_stagingOutputPath))
        {
            try { File.Delete(_stagingOutputPath); } catch { }
        }

        _disposed = true;
    }

    private static void ValidateRequest(VideoProcessingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.InputFile))
            throw new ArgumentException("输入文件路径不能为空", nameof(request.InputFile));
        if (string.IsNullOrWhiteSpace(request.OutputFile))
            throw new ArgumentException("输出文件路径不能为空", nameof(request.OutputFile));
        if (request.Width <= 0 || request.Width > 8192)
            throw new ArgumentOutOfRangeException(nameof(request.Width), "ASCII 宽度必须在 1 到 8192 之间");
        if (request.Height < 0 || request.Height > 8192)
            throw new ArgumentOutOfRangeException(nameof(request.Height), "ASCII 高度必须在 0 到 8192 之间");
        if (!double.IsFinite(request.FrameRate) || request.FrameRate < 0 || request.FrameRate > 1000)
            throw new ArgumentOutOfRangeException(nameof(request.FrameRate), "帧率必须在 0 到 1000 之间");
        if (!float.IsFinite(request.FontSize) || request.FontSize <= 0 || request.FontSize > 512)
            throw new ArgumentOutOfRangeException(nameof(request.FontSize), "字体大小必须在 0 到 512 之间");
        if (request.MaxFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(request.MaxFrames), "最大帧数不能为负数");
        if (!string.Equals(request.CharSet, "standard", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.CharSet, "detailed", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("字符集只能是 standard 或 detailed", nameof(request.CharSet));
    }
}

/// <summary>
/// 性能统计数据
/// </summary>
public record PerformanceStats
{
    public double DecodeTimeMs { get; init; }
    public double GrayscaleTimeMs { get; init; }
    public double MappingTimeMs { get; init; }
    public double RenderTimeMs { get; init; }
    public double EncodeTimeMs { get; init; }

    /// <summary>总耗时（毫秒）</summary>
    public double TotalTimeMs =>
        DecodeTimeMs + GrayscaleTimeMs + MappingTimeMs +
        RenderTimeMs + EncodeTimeMs;

    /// <summary>每帧平均耗时</summary>
    public double AvgFrameTimeMs(int totalFrames) =>
        totalFrames > 0 ? (double)TotalTimeMs / totalFrames : 0;
}
