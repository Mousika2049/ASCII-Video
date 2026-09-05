using System;
using FFmpeg.AutoGen;

namespace AsciiFlow.Core.Video;

/// <summary>
/// FFmpeg 库初始化器（全局单例）
/// </summary>
public static class FFmpegInitializer
{
    private static string? _ffmpegRootPath;
    private static bool _initialized = false;
    private static readonly object _lock = new();

    /// <summary>
    /// 错误码常量（FFmpeg 标准错误码）
    /// </summary>
    public static class ErrorCode
    {
        /// <summary>AVERROR_EOF - 文件结束</summary>
        public static readonly int EOF = ffmpeg.AVERROR_EOF;

        /// <summary>AVERROR_EAGAIN - 需要更多数据</summary>
        public const int EAGAIN = -11;

        /// <summary>AVERROR_EIO - I/O 错误</summary>
        public const int EIO = -5;

        /// <summary>AVERROR_EINVAL - 无效参数</summary>
        public const int EINVAL = -22;
    }

    /// <summary>
    /// 初始化 FFmpeg 库
    /// </summary>
    /// <param name="ffmpegRootPath">FFmpeg 动态库路径</param>
    public static void Initialize(string? ffmpegRootPath = null)
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            if (!string.IsNullOrEmpty(ffmpegRootPath))
            {
                _ffmpegRootPath = ffmpegRootPath;
                ffmpeg.RootPath = ffmpegRootPath;
            }

            try
            {
                // FFmpeg.AutoGen 通过访问任意原生方法触发静态绑定初始化。
                _ = ffmpeg.av_version_info();

                _initialized = true;
            }
            catch (DllNotFoundException ex)
            {
                // 使用 FFmpegPathResolver 的详细帮助信息
                throw new DllNotFoundException(
                    $"FFmpeg 动态库加载失败：{ex.Message}\n\n{FFmpegPathResolver.GetHelpMessage()}", ex);
            }
            catch (NotSupportedException ex)
            {
                // "Specified method is not supported" 通常是 ABI 版本不匹配
                throw new NotSupportedException(
                    $"FFmpeg ABI 不兼容：{ex.Message}\n\n" +
                    "可能原因：FFmpeg.AutoGen 9.0.1 与当前加载的 FFmpeg 共享库 ABI 不匹配。\n" +
                    "请确认绑定与整套 libav* 共享库来自兼容版本。\n\n" +
                    "注意：ffmpeg -version 只显示命令行程序版本，不保证应用加载的是同一套共享库。\n" +
                    FFmpegPathResolver.GetHelpMessage(), ex);
            }
        }
    }

    /// <summary>
    /// 重置初始化状态（仅用于测试）
    /// </summary>
    internal static void Reset()
    {
        lock (_lock)
        {
            _initialized = false;
            _ffmpegRootPath = null;
        }
    }
}
