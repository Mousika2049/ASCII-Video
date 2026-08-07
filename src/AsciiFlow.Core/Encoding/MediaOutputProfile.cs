namespace AsciiFlow.Core.Encoding;

/// <summary>
/// 将输出文件扩展名映射为 FFmpeg 容器和视频编码器。
/// </summary>
public sealed record MediaOutputProfile(
    string Extension,
    string ContainerFormat,
    string ContainerDisplayName,
    string VideoCodecName,
    string VideoCodecDisplayName)
{
    private static readonly IReadOnlyDictionary<string, MediaOutputProfile> Profiles =
        new Dictionary<string, MediaOutputProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [".mp4"] = H264(".mp4", "mp4", "MP4"),
            [".m4v"] = H264(".m4v", "mp4", "M4V"),
            [".mov"] = H264(".mov", "mov", "MOV"),
            [".mkv"] = H264(".mkv", "matroska", "Matroska"),
            [".avi"] = H264(".avi", "avi", "AVI"),
            [".ts"] = H264(".ts", "mpegts", "MPEG-TS"),
            [".m2ts"] = H264(".m2ts", "mpegts", "MPEG-TS"),
            [".webm"] = new(
                ".webm", "webm", "WebM", "libvpx-vp9", "VP9")
        };

    public static IReadOnlyCollection<string> SupportedExtensions { get; } =
        Profiles.Keys.Order(StringComparer.Ordinal).ToArray();

    public bool IsVp9 => string.Equals(VideoCodecName, "libvpx-vp9", StringComparison.Ordinal);

    public string FormatEncoderSettings(VideoEncoderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!IsVp9)
        {
            string tune = string.IsNullOrEmpty(settings.Tune)
                ? string.Empty
                : $" · tune {settings.Tune}";
            return $"{settings.Preset} · CRF {settings.Crf}{tune}";
        }

        string deadline = string.Equals(settings.Mode, "speed", StringComparison.Ordinal)
            ? "realtime"
            : "good";
        string cpuUsed = settings.Mode switch
        {
            "speed" => "8",
            "balanced" => "6",
            _ => "4"
        };
        return $"{deadline} · cpu-used {cpuUsed} · CRF {settings.Crf}";
    }

    public static MediaOutputProfile FromPath(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string extension = Path.GetExtension(outputPath);
        if (Profiles.TryGetValue(extension, out MediaOutputProfile? profile))
            return profile;

        string supported = string.Join(", ", SupportedExtensions);
        string actual = string.IsNullOrEmpty(extension) ? "无扩展名" : extension;
        throw new NotSupportedException(
            $"不支持输出格式 {actual}；可用格式：{supported}");
    }

    private static MediaOutputProfile H264(string extension, string container, string displayName) =>
        new(extension, container, displayName, "libx264", "H.264");
}
