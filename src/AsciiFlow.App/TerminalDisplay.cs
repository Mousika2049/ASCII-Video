namespace AsciiFlow.App;

internal static class TerminalDisplay
{
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
