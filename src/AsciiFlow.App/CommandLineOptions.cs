using CommandLine;
using AsciiFlow.App.Core;

namespace AsciiFlow.App;

/// <summary>
/// 命令行选项定义
/// </summary>
public class CommandLineOptions
{
    [Option('i', "input", Required = true, HelpText = "输入视频文件路径")]
    public string InputFile { get; set; } = string.Empty;

    [Option('o', "output", Required = false, Default = "output/output_ascii.mp4", HelpText = "输出视频文件路径")]
    public string OutputFile { get; set; } = "output/output_ascii.mp4";

    [Option('w', "width", Required = false, Default = 240, HelpText = "ASCII 字符画宽度（字符数，默认 240 超高清）")]
    public int Width { get; set; } = 240;

    [Option('h', "height", Required = false, Default = 0, HelpText = "ASCII 字符画高度（字符数，0 = 自动根据原视频比例推算，16:9 对应 135）")]
    public int Height { get; set; } = 0;

    [Option('f', "framerate", Required = false, Default = 0.0, HelpText = "输出视频帧率（0 = 保持与原视频一致）")]
    public double FrameRate { get; set; } = 0.0;

    [Option('c', "charset", Required = false, Default = "standard",
        HelpText = "字符集: standard(70字符) 或 detailed(16字符)")]
    public string CharSet { get; set; } = "standard";

    [Option("font-size", Required = false, Default = 12.0f, HelpText = "渲染字体大小（像素）")]
    public float FontSize { get; set; } = 12.0f;

    [Option("font-family", Required = false, Default = "Consolas", HelpText = "渲染字体族")]
    public string FontFamily { get; set; } = "Consolas";

    [Option("max-frames", Required = false, Default = 0, HelpText = "最大处理帧数（0 = 全部）")]
    public int MaxFrames { get; set; } = 0;

    [Option('C', "color", Required = false, Default = "true", HelpText = "是否启用彩色 ASCII 模式：true 或 false（默认 true）")]
    public string ColorValue { get; set; } = "true";

    public bool Color => bool.TryParse(ColorValue, out bool value)
        ? value
        : throw new ArgumentException("--color 只能是 true 或 false", nameof(ColorValue));

    [Option('v', "verbose", Required = false, Default = false, HelpText = "显示详细日志")]
    public bool Verbose { get; set; } = false;

    [Option("no-progress", Required = false, Default = false, HelpText = "禁用进度显示")]
    public bool NoProgress { get; set; } = false;

    public VideoProcessingRequest ToProcessingRequest() => new()
    {
        InputFile = InputFile,
        OutputFile = OutputFile,
        Width = Width,
        Height = Height,
        FrameRate = FrameRate,
        CharSet = CharSet,
        FontSize = FontSize,
        FontFamily = FontFamily,
        MaxFrames = MaxFrames,
        Color = Color,
        Verbose = Verbose,
        NoProgress = NoProgress
    };
}
