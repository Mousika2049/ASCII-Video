using System;
using System.Diagnostics;
using System.IO;
using CommandLine;
using AsciiFlow.App.Core;
using AsciiFlow.Core.Encoding;
using AsciiFlow.Core.Video;

namespace AsciiFlow.App;

/// <summary>
/// AsciiFlow 主程序入口
/// ASCII 视频转换器，将普通视频转换为 ASCII 风格视频
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            return Parser.Default.ParseArguments<CommandLineOptions>(args)
                .MapResult(
                    opts => Run(opts, cancellationSource.Token),
                    _ => 2);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// 自动检测并设置 FFmpeg 动态库路径
    /// </summary>
    private static void SetupFFmpegRootPath(bool verbose)
    {
        try
        {
            string? resolvedPath = FFmpegPathResolver.Resolve();

            if (resolvedPath != null)
            {
                FFmpeg.AutoGen.ffmpeg.RootPath = resolvedPath;
                if (verbose)
                    Console.WriteLine($"FFmpeg  {resolvedPath}");
            }
            else
            {
                Console.Error.WriteLine(FFmpegPathResolver.GetHelpMessage());
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"警告    FFmpeg 路径检测失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 主执行流程
    /// </summary>
    static int Run(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var pipeline = new VideoPipeline();

        try
        {
            PrintRequest(options);
            SetupFFmpegRootPath(options.Verbose);

            // 初始化流水线
            VideoProcessingRequest request = options.ToProcessingRequest();
            pipeline.Initialize(request);

            Console.WriteLine("处理    正在转换...");

            // 处理视频
            int totalFrames = pipeline.Process(request, cancellationToken);

            // 完成编码（使用 Finish 而不是 Finalize，避免与 object.Finalize 冲突）
            pipeline.WriteTrailer();

            stopwatch.Stop();

            var stats = pipeline.GetStatistics();
            PrintCompletion(options, stats, totalFrames, stopwatch);

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("取消    处理已取消，原输出文件未被替换");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误    {ex.Message}");

            if (options.Verbose)
            {
                Console.Error.WriteLine($"类型    {ex.GetType().FullName}");
                Console.Error.WriteLine(ex.StackTrace);

                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine(
                        $"内部    {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }
            else
            {
                Console.Error.WriteLine("提示    使用 --verbose 查看诊断信息");
            }

            return 1;
        }
        finally
        {
            pipeline.Dispose();
        }
    }

    private static void PrintRequest(CommandLineOptions options)
    {
        string heightText = options.Height > 0 ? options.Height.ToString() : "自动";
        string maxFramesText = options.MaxFrames > 0 ? $" · 最多 {options.MaxFrames} 帧" : string.Empty;

        Console.WriteLine("AsciiFlow 1.0.0");
        Console.WriteLine($"输入    {Path.GetFullPath(options.InputFile)}");
        Console.WriteLine($"保存至  {Path.GetFullPath(options.OutputFile)}");
        Console.WriteLine(
            $"配置    ASCII {options.Width}x{heightText} · " +
            $"{(options.Color ? "彩色" : "黑白")} · {options.EncoderMode}{maxFramesText}");

        if (options.Verbose)
        {
            string frameRateText = options.FrameRate > 0 ? $"{options.FrameRate:F3} FPS" : "跟随源视频";
            Console.WriteLine(
                $"选项    {options.CharSet} · {options.FontFamily} {options.FontSize:F1}px · {frameRateText}");
        }

        Console.WriteLine();
    }

    private static void PrintCompletion(
        CommandLineOptions options,
        PerformanceStats stats,
        int totalFrames,
        Stopwatch stopwatch)
    {
        double seconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        string outputPath = Path.GetFullPath(options.OutputFile);
        long? outputBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : null;

        Console.WriteLine($"完成    {TerminalDisplay.FormatCompletion(totalFrames, seconds, outputBytes)}");
        Console.WriteLine($"文件    {outputPath}");

        if (options.Verbose)
            PrintPerformanceDetails(
                stats,
                totalFrames,
                MediaOutputProfile.FromPath(options.OutputFile).VideoCodecDisplayName);
    }

    private static void PrintPerformanceDetails(
        PerformanceStats stats,
        int totalFrames,
        string videoCodecDisplayName)
    {
        Console.WriteLine();
        Console.WriteLine("性能明细（每帧）");
        Console.WriteLine($"  解码            {stats.DecodeTimeMs / totalFrames,7:F2} ms");
        Console.WriteLine($"  灰度/颜色映射   {stats.MappingTimeMs / totalFrames,7:F2} ms");
        Console.WriteLine($"  字符渲染        {stats.RenderTimeMs / totalFrames,7:F2} ms");
        Console.WriteLine($"  编码总计        {stats.EncodeTimeMs / totalFrames,7:F2} ms");
        Console.WriteLine($"    RGB → YUV     {stats.ColorConversionTimeMs / totalFrames,7:F2} ms");
        Console.WriteLine($"    {videoCodecDisplayName,-15}{stats.CodecTimeMs / totalFrames,7:F2} ms");
        Console.WriteLine($"    视频封装       {stats.MuxTimeMs / totalFrames,7:F2} ms");
        Console.WriteLine($"  编码收尾        {stats.EncoderFinishTimeMs,7:F2} ms");
    }

}
