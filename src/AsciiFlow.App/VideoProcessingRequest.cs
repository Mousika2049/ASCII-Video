namespace AsciiFlow.App.Core;

/// <summary>
/// 与命令行解析器无关的视频处理请求。
/// </summary>
public sealed record VideoProcessingRequest
{
    public required string InputFile { get; init; }
    public required string OutputFile { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }
    public required string CharSet { get; init; }
    public float FontSize { get; init; }
    public required string FontFamily { get; init; }
    public int MaxFrames { get; init; }
    public string EncoderMode { get; init; } = "speed";
    public bool Color { get; init; }
    public bool Verbose { get; init; }
    public bool NoProgress { get; init; }
}
