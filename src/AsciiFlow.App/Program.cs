using System;
using System.Diagnostics;
using System.IO;
using CommandLine;
using AsciiFlow.App.Core;
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
    private static void SetupFFmpegRootPath()
    {
        try
        {
            string? resolvedPath = FFmpegPathResolver.Resolve();

            if (resolvedPath != null)
            {
                FFmpeg.AutoGen.ffmpeg.RootPath = resolvedPath;
                Console.WriteLine($"[FFmpeg] ✓ 动态库路径: {resolvedPath}");
            }
            else
            {
                // 未找到路径 —— 显示详细帮助
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(FFmpegPathResolver.GetHelpMessage());
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[FFmpeg] ⚠ 路径检测异常: {ex.Message}");
            Console.ResetColor();
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
            SetupFFmpegRootPath();
            Console.WriteLine("╔══════════════════════════════════════════════╗");
            Console.WriteLine("║     AsciiFlow - ASCII 视频转换器 v1.0.0      ║");
            Console.WriteLine("╚══════════════════════════════════════════════╝");
            Console.WriteLine();

            // 打印配置信息
            PrintConfiguration(options);

            // 初始化流水线
            VideoProcessingRequest request = options.ToProcessingRequest();
            pipeline.Initialize(request);

            Console.WriteLine();
            Console.WriteLine("开始处理...");
            Console.WriteLine(new string('─', 50));

            // 处理视频
            int totalFrames = pipeline.Process(request, cancellationToken);

            // 完成编码（使用 Finish 而不是 Finalize，避免与 object.Finalize 冲突）
            pipeline.WriteTrailer();

            stopwatch.Stop();

            // 打印性能报告
            var stats = pipeline.GetStatistics();
            PrintPerformanceReport(stats, totalFrames, stopwatch);

            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════╗");
            Console.WriteLine("║              ✅ 处理完成！                    ║");
            Console.WriteLine("╚══════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"输出文件: {Path.GetFullPath(options.OutputFile)}");

            if (File.Exists(options.OutputFile))
            {
                Console.WriteLine($"文件大小: {new FileInfo(options.OutputFile).Length / 1024.0 / 1024.0:F2} MB");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("处理已取消，未替换原输出文件。");
            return 130;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"╔══════════════════════════════════════════════╗");
            Console.WriteLine($"║  ❌ 处理失败！                                ║");
            Console.WriteLine($"╚══════════════════════════════════════════════╝");
            Console.WriteLine();

            if (options.Verbose)
            {
                Console.WriteLine($"错误类型: {ex.GetType().Name}");
                Console.WriteLine($"错误信息: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("堆栈跟踪:");
                Console.WriteLine(ex.StackTrace);

                if (ex.InnerException != null)
                {
                    Console.WriteLine();
                    Console.WriteLine("内部异常:");
                    Console.WriteLine($"  类型: {ex.InnerException.GetType().Name}");
                    Console.WriteLine($"  信息: {ex.InnerException.Message}");
                }
            }
            else
            {
                Console.WriteLine($"错误信息: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("提示: 使用 --verbose 选项查看详细信息");
            }

            return 1;
        }
        finally
        {
            pipeline?.Dispose();
        }
    }

    /// <summary>
    /// 打印配置信息
    /// </summary>
    static void PrintConfiguration(CommandLineOptions options)
    {
        Console.WriteLine("【配置信息】");
        Console.WriteLine($"  输入文件: {options.InputFile}");
        Console.WriteLine($"  输出文件: {options.OutputFile}");
        string heightText = options.Height > 0 ? $"{options.Height}" : "自动";
        Console.WriteLine($"  ASCII 尺寸: {options.Width} × {heightText} 字符");
        Console.WriteLine($"  输出尺寸: 遵循原视频分辨率");
        string fpsText = options.FrameRate > 0 ? $"{options.FrameRate} fps" : "自动（与原视频保持一致）";
        Console.WriteLine($"  帧率: {fpsText}");
        Console.WriteLine($"  字符集: {options.CharSet}");
        Console.WriteLine($"  字体: {options.FontFamily} {options.FontSize}px");
        Console.WriteLine($"  彩色模式: {(options.Color ? "开启" : "关闭")}");

        if (options.MaxFrames > 0)
        {
            Console.WriteLine($"  最大帧数: {options.MaxFrames}");
        }

        Console.WriteLine();
        Console.WriteLine("【处理流水线】");
        Console.WriteLine("  [解码] FFmpeg → 并行灰度 → ASCII映射 → SkiaSharp渲染 → H.264编码 [输出]");
    }

    /// <summary>
    /// 打印性能报告
    /// </summary>
    static void PrintPerformanceReport(PerformanceStats stats, int totalFrames, Stopwatch stopwatch)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 50));
        Console.WriteLine("                    📊 性能报告");
        Console.WriteLine(new string('═', 50));
        Console.WriteLine();

        // ====== [修复] 零帧数保护 ======
        if (totalFrames == 0)
        {
            Console.WriteLine("【总览】");
            Console.WriteLine("  处理帧数: 0 帧");
            Console.WriteLine("  ⚠️  警告: 未处理任何帧");
            Console.WriteLine();
            Console.WriteLine("可能原因：");
            Console.WriteLine("  • 输入视频为空或无效");
            Console.WriteLine("  • 解码器未能读取任何帧");
            Console.WriteLine("  • --max-frames 设置为 0 且视频读取立即失败");
            return;
        }
        // =================================

        double totalSeconds = stopwatch.Elapsed.TotalSeconds;
        if (totalSeconds <= 0) totalSeconds = 0.001; // 防止除零

        Console.WriteLine($"【总览】");
        Console.WriteLine($"  处理帧数: {totalFrames} 帧");
        Console.WriteLine($"  总耗时: {totalSeconds:F2} 秒");
        Console.WriteLine($"  平均 FPS: {(totalFrames / totalSeconds):F2}");
        Console.WriteLine();

        Console.WriteLine($"【各阶段耗时】");
        Console.WriteLine($"  ├─ 解码: {stats.DecodeTimeMs / 1000.0:F3}s ({stats.DecodeTimeMs / (double)totalFrames:F2}ms/帧)");
        Console.WriteLine($"  ├─ 灰度转换: {stats.GrayscaleTimeMs / 1000.0:F3}s ({stats.GrayscaleTimeMs / (double)totalFrames:F2}ms/帧)");
        Console.WriteLine($"  ├─ ASCII映射: {stats.MappingTimeMs / 1000.0:F3}s ({stats.MappingTimeMs / (double)totalFrames:F2}ms/帧)");
        Console.WriteLine($"  ├─ 渲染: {stats.RenderTimeMs / 1000.0:F3}s ({stats.RenderTimeMs / (double)totalFrames:F2}ms/帧)");
        Console.WriteLine($"  └─ 编码: {stats.EncodeTimeMs / 1000.0:F3}s ({stats.EncodeTimeMs / (double)totalFrames:F2}ms/帧)");
        Console.WriteLine();

        double avgFrameTime = (stats.DecodeTimeMs + stats.GrayscaleTimeMs +
                               stats.MappingTimeMs + stats.RenderTimeMs + stats.EncodeTimeMs)
                              / (double)totalFrames;
        double theoreticalFps = avgFrameTime > 0 ? 1000.0 / avgFrameTime : 0;

        Console.WriteLine($"【性能评估】");
        Console.WriteLine($"  单帧平均耗时: {avgFrameTime:F2}ms");
        Console.WriteLine($"  理论 FPS: {theoreticalFps:F1}");
        Console.WriteLine();

        if (avgFrameTime <= 30)
        {
            Console.WriteLine("  ✅ 当前配置的处理速度高于 30 FPS 实时基准");
        }
        else if (avgFrameTime <= 50)
        {
            Console.WriteLine("  ✅ 当前配置接近 20–30 FPS");
        }
        else if (avgFrameTime <= 100)
        {
            Console.WriteLine("  ⚠️  性能一般！建议降低 ASCII 分辨率");
        }
        else
        {
            Console.WriteLine("  ❌ 性能不足！建议:");
            Console.WriteLine("     • 降低 ASCII 分辨率（如 40x20）");
            Console.WriteLine("     • 降低输出帧率（如 15fps）");
            Console.WriteLine("     • 检查 CPU 性能和 FFmpeg 编解码器");
        }
    }
}
