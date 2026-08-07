namespace AsciiFlow.Core.Encoding;

/// <summary>
/// 根据输出分辨率和可用 CPU 为 libvpx-vp9 生成并行编码参数。
/// </summary>
public sealed record Vp9EncoderTuning(
    string Deadline,
    int CpuUsed,
    int ThreadCount,
    int TileColumns,
    int? LagInFrames)
{
    public int TileCount => 1 << TileColumns;

    public static Vp9EncoderTuning Create(
        VideoEncoderSettings settings,
        int width,
        int height,
        int processorCount)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (processorCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(processorCount));

        long pixels = (long)width * height;
        int usefulThreads = pixels switch
        {
            <= 640L * 360 => 2,
            <= 1280L * 720 => 8,
            <= 1920L * 1080 => 12,
            _ => 16
        };
        int threadCount = Math.Min(processorCount, usefulThreads);

        int tileColumns = width switch
        {
            >= 2560 => 3,
            >= 960 => 2,
            >= 480 => 1,
            _ => 0
        };
        while (tileColumns > 0 &&
               ((1 << tileColumns) > threadCount || (1 << tileColumns) * 256 > width))
        {
            tileColumns--;
        }

        string deadline = string.Equals(settings.Mode, "speed", StringComparison.Ordinal)
            ? "realtime"
            : "good";
        int cpuUsed = settings.Mode switch
        {
            "speed" => 8,
            "balanced" => 6,
            _ => 4
        };
        int? lagInFrames = string.Equals(settings.Mode, "speed", StringComparison.Ordinal)
            ? 0
            : null;

        return new Vp9EncoderTuning(
            deadline,
            cpuUsed,
            threadCount,
            tileColumns,
            lagInFrames);
    }
}
