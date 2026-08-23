namespace AsciiFlow.App;

internal static class TerminalDisplay
{
    internal static string FormatSourceFrameCount(long frameCount, long? estimatedFrameCount)
    {
        if (frameCount > 0)
            return $"{frameCount} 帧";

        return estimatedFrameCount > 0
            ? $"帧数未知（按时长估算约 {estimatedFrameCount} 帧）"
            : "帧数未知";
    }

    internal static string FormatProgress(
        int processedFrames,
        long? totalFrames,
        bool totalIsEstimated,
        double framesPerSecond,
        int barWidth = 30)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(processedFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(barWidth);

        double percentage = totalFrames > 0
            ? Math.Min(totalIsEstimated ? 99.9 : 100, (double)processedFrames / totalFrames.Value * 100)
            : 0;
        int filled = (int)(percentage / 100 * barWidth);
        string bar = new string('█', filled) + new string('░', barWidth - filled);
        string percentageText = totalIsEstimated ? $"约 {percentage:F1}" : $"{percentage,5:F1}";
        string totalFramesText = totalFrames > 0
            ? $"{(totalIsEstimated ? "约 " : string.Empty)}{totalFrames}"
            : "?";

        return $"处理    [{bar}] {percentageText}% · " +
               $"{processedFrames}/{totalFramesText} 帧 · {framesPerSecond:F0} FPS";
    }

    internal static string FormatFileSize(long bytes)
    {
        const double kibibyte = 1024;
        const double mebibyte = kibibyte * 1024;
        return bytes >= mebibyte
            ? $"{bytes / mebibyte:F2} MB"
            : $"{bytes / kibibyte:F1} KB";
    }

    internal static string FormatCompletion(int frames, double seconds, long? outputBytes)
    {
        double safeSeconds = Math.Max(0.001, seconds);
        string sizeText = outputBytes.HasValue ? FormatFileSize(outputBytes.Value) : "大小未知";
        return $"{frames} 帧 · {safeSeconds:F2}s · {frames / safeSeconds:F1} FPS · {sizeText}";
    }
}
