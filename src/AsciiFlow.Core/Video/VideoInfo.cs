namespace AsciiFlow.Core.Video;

/// <summary>
/// 精确的视频帧率。使用有理数避免 30000/1001 等常见帧率被截断。
/// </summary>
public readonly record struct VideoFrameRate(int Numerator, int Denominator)
{
    public bool IsValid => Numerator > 0 && Denominator > 0;
    public double Value => IsValid ? (double)Numerator / Denominator : 0;

    public static VideoFrameRate FromDouble(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            return default;

        decimal decimalValue = (decimal)value;
        int[] bits = decimal.GetBits(decimalValue);
        int scale = (bits[3] >> 16) & 0x7F;
        long denominator = 1;
        for (int i = 0; i < scale && denominator <= 100_000; i++)
            denominator *= 10;

        long numerator = (long)Math.Round(value * denominator);
        long gcd = GreatestCommonDivisor(numerator, denominator);
        numerator /= gcd;
        denominator /= gcd;

        if (numerator > int.MaxValue || denominator > int.MaxValue)
            return new VideoFrameRate((int)Math.Round(value * 1000), 1000).Reduce();

        return new VideoFrameRate((int)numerator, (int)denominator).Reduce();
    }

    public VideoFrameRate Reduce()
    {
        if (!IsValid) return default;
        long gcd = GreatestCommonDivisor(Numerator, Denominator);
        return new VideoFrameRate((int)(Numerator / gcd), (int)(Denominator / gcd));
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
            (left, right) = (right, left % right);
        return Math.Max(1, left);
    }

    public override string ToString() => IsValid ? $"{Numerator}/{Denominator}" : "unknown";
}

/// <summary>
/// 视频信息类
/// </summary>
public class VideoInfo
{
    /// <summary>视频宽度</summary>
    public int Width { get; set; }

    /// <summary>视频高度</summary>
    public int Height { get; set; }

    /// <summary>视频帧率（FPS）</summary>
    public double FrameRate { get; set; }

    /// <summary>未经过浮点舍入的帧率</summary>
    public VideoFrameRate ExactFrameRate { get; set; }

    /// <summary>视频总帧数</summary>
    public long FrameCount { get; set; }

    /// <summary>视频时长（秒）</summary>
    public double DurationSeconds { get; set; }

    /// <summary>视频编码格式</summary>
    public string CodecName { get; set; } = string.Empty;

    /// <summary>像素格式</summary>
    public string PixelFormat { get; set; } = string.Empty;

    /// <summary>
    /// 获取视频分辨率字符串
    /// </summary>
    public string Resolution => $"{Width}x{Height}";

    /// <summary>
    /// 创建视频信息实例
    /// </summary>
    public VideoInfo() { }

    /// <summary>
    /// 创建视频信息实例
    /// </summary>
    public VideoInfo(
        int width,
        int height,
        VideoFrameRate frameRate,
        long frameCount,
        string codecName = "",
        string pixelFormat = "")
    {
        Width = width;
        Height = height;
        ExactFrameRate = frameRate.Reduce();
        FrameRate = ExactFrameRate.Value;
        FrameCount = frameCount;
        CodecName = codecName;
        PixelFormat = pixelFormat;
        DurationSeconds = FrameRate > 0 && frameCount > 0 ? frameCount / FrameRate : 0;
    }

    public VideoInfo(int width, int height, double frameRate, long frameCount, string codecName = "", string pixelFormat = "")
        : this(width, height, VideoFrameRate.FromDouble(frameRate), frameCount, codecName, pixelFormat)
    {
    }

    /// <summary>
    /// 重写 ToString 方法
    /// </summary>
    public override string ToString()
    {
        return $"Video: {Resolution}, {FrameRate:F2} FPS, {DurationSeconds:F2}s, {FrameCount} frames, {CodecName}";
    }
}
