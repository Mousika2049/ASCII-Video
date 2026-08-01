namespace AsciiFlow.Core.Encoding;

/// <summary>
/// libx264 编码参数。预设模式均经过 AsciiFlow 的 1080p 彩色与黑白样本验证。
/// </summary>
public sealed record VideoEncoderSettings(
    string Mode,
    string Preset,
    int Crf,
    string? Tune)
{
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
        null);

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
