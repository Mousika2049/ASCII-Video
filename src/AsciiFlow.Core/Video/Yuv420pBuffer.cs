namespace AsciiFlow.Core.Video;

/// <summary>
/// 连续平面 YUV420P 缓冲区布局：Y 平面，随后是 U 和 V 平面。
/// </summary>
public static class Yuv420pBuffer
{
    public static int GetSize(int width, int height)
    {
        ValidateDimensions(width, height);
        int lumaSize = checked(width * height);
        return checked(lumaSize + lumaSize / 2);
    }

    public static void Validate(byte[] data, int width, int height, string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(data, parameterName);
        int expectedSize = GetSize(width, height);
        if (data.Length != expectedSize)
        {
            throw new ArgumentException(
                $"YUV420P 数据长度错误: 期望 {expectedSize}, 实际 {data.Length}",
                parameterName);
        }
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"尺寸不合法: {width}x{height}");
        if ((width & 1) != 0 || (height & 1) != 0)
            throw new ArgumentException($"YUV420P 尺寸必须为偶数: {width}x{height}");
    }
}
