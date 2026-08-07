namespace AsciiFlow.Core.Encoding;

/// <summary>
/// 视频编码质量/速度档位。H.264 使用 preset/tune，VP9 会映射为对应的 deadline/cpu-used。
/// </summary>
public sealed record VideoEncoderSettings(
    string Mode,
    string Preset,
    int Crf,
    string? Tune)
{
    public int MaxBFrames { get; init; } = 2;

    /// <summary>折中模式：视觉质量达标，同时控制文件体积。</summary>
    public static VideoEncoderSettings Balanced { get; } = new(
        "balanced",
        "superfast",
        20,
        null);

    /// <summary>旧版质量参数，输出更小但编码明显更慢。</summary>
    public static VideoEncoderSettings Quality { get; } = new(
        "quality",
        "fast",
        23,
        "fastdecode");

    /// <summary>默认模式：通过质量门槛的最高吞吐参数，文件通常显著大于 balanced。</summary>
    public static VideoEncoderSettings Speed { get; } = new(
        "speed",
        "ultrafast",
        20,
        null)
    {
        MaxBFrames = 0
    };

    public static VideoEncoderSettings FromMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            throw new ArgumentException("编码模式不能为空", nameof(mode));

        return mode.ToLowerInvariant() switch
        {
            "balanced" => Balanced,
            "quality" => Quality,
            "speed" => Speed,
            _ => throw new ArgumentException(
                "编码模式只能是 balanced、quality 或 speed",
                nameof(mode))
        };
    }
}
