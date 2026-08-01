namespace AsciiFlow.Core.AsciiMapping;

/// <summary>
/// 基于查找表的高性能 ASCII 字符映射器
/// </summary>
public class LookupTableAsciiMapper : IAsciiMapper
{
    private const int RedLuminanceCoefficient = 54;
    private const int GreenLuminanceCoefficient = 183;
    private const int BlueLuminanceCoefficient = 19;
    private readonly char[] _characterSet;
    private readonly char[] _lookupTable;
    private readonly ParallelOptions _parallelOptions;
    private char[] _characterBuffer = [];
    private (byte R, byte G, byte B)[] _colorBuffer = [];
    private AsciiFrame? _monochromeFrame;
    private AsciiFrame? _colorFrame;

    /// <summary>
    /// 字符集预设（由暗到亮排序：适合黑底白字/彩色 ASCII 渲染）
    /// </summary>
    public static readonly string Standard =
        " .'`^\"^:;Il!i><~+_-?[]{}1()|/\\tfjrxnuvczXYUJCLQ0OZmwqpbdkhao*#MW&8%B@$";

    public static readonly string Detailed =
        " .:-=+*#%@$WMB8";

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="characterSet">
    /// 字符集字符串（默认使用 Standard）
    /// </param>
    /// <param name="maxDegreeOfParallelism">最大并行度（CPU 核心数）</param>
    public LookupTableAsciiMapper(
        string? characterSet = null,
        int maxDegreeOfParallelism = 0)
    {
        _characterSet = (characterSet ?? Standard).ToCharArray();
        if (_characterSet.Length == 0)
            throw new ArgumentException("字符集不能为空", nameof(characterSet));
        _lookupTable = BuildLookupTable(_characterSet);

        _parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism > 0
                ? maxDegreeOfParallelism
                : Environment.ProcessorCount
        };
    }

    /// <summary>
    /// 构建查找表（结合 S-Curve 伽马对比度增强算法）
    /// </summary>
    private static char[] BuildLookupTable(char[] charset)
    {
        var table = new char[256];
        int charsetLength = charset.Length;

        for (int grayValue = 0; grayValue < 256; grayValue++)
        {
            // S-Curve 对比度增强：增强暗部与高光的梯度的清晰度
            double norm = grayValue / 255.0;
            double sCurve = norm < 0.5
                ? 2.0 * norm * norm
                : 1.0 - 2.0 * (1.0 - norm) * (1.0 - norm);

            int index = (int)(sCurve * (charsetLength - 1) + 0.5);
            table[grayValue] = charset[Math.Clamp(index, 0, charsetLength - 1)];
        }

        return table;
    }

    /// <summary>
    /// 将灰度数据映射为 ASCII 字符串
    /// </summary>
    public string MapToAscii(
        byte[] grayData,
        int width,
        int height,
        int targetWidth,
        int targetHeight)
    {
        AsciiFrame frame = Map(grayData, width, height, targetWidth, targetHeight);
        return ToAsciiString(frame);
    }

    /// <summary>
    /// 将图像映射为 ASCII 字符串，并同时采样每个字符单元格的原视频 RGB 颜色（用于彩色 ASCII）
    /// </summary>
    public (string AsciiArt, (byte R, byte G, byte B)[] Colors) MapToAsciiWithColor(
        byte[] rgbData,
        byte[] grayData,
        int width,
        int height,
        int targetWidth,
        int targetHeight)
    {
        AsciiFrame frame = Map(grayData, width, height, targetWidth, targetHeight, rgbData);
        return (ToAsciiString(frame), frame.Colors!);
    }

    public AsciiFrame Map(
        byte[] grayData,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        byte[]? rgbData = null)
    {
        ValidateParameters(grayData, rgbData, width, height, targetWidth, targetHeight);

        int cellCount = checked(targetWidth * targetHeight);
        if (_characterBuffer.Length != cellCount)
            _characterBuffer = new char[cellCount];

        bool includeColor = rgbData is not null;
        if (includeColor && _colorBuffer.Length != cellCount)
            _colorBuffer = new (byte R, byte G, byte B)[cellCount];

        unsafe
        {
            fixed (byte* rgbPtr = rgbData)
            fixed (byte* grayPtr = grayData)
            {
                IntPtr rgbAddr = (IntPtr)rgbPtr;
                IntPtr grayAddr = (IntPtr)grayPtr;

                Parallel.For(0, targetHeight, _parallelOptions, targetY =>
                {
                    byte* rPtr = (byte*)rgbAddr;
                    byte* gPtr = (byte*)grayAddr;

                    int srcY = targetY * height / targetHeight;
                    int endY = (targetY + 1) * height / targetHeight;

                    for (int targetX = 0; targetX < targetWidth; targetX++)
                    {
                        int srcX = targetX * width / targetWidth;
                        int endX = (targetX + 1) * width / targetWidth;

                        long sumR = 0, sumG = 0, sumB = 0, sumGray = 0;
                        int count = 0;

                        for (int y = srcY; y < endY; y++)
                        {
                            int rowOffset = y * width;
                            int rowRgbOffset = rowOffset * 3;

                            for (int x = srcX; x < endX; x++)
                            {
                                int pixelIdx = rowOffset + x;
                                int rgbIdx = x * 3 + rowRgbOffset;

                                if (includeColor)
                                {
                                    sumR += rPtr[rgbIdx];
                                    sumG += rPtr[rgbIdx + 1];
                                    sumB += rPtr[rgbIdx + 2];
                                }
                                sumGray += gPtr[pixelIdx];
                                count++;
                            }
                        }

                        byte avgGray = count > 0 ? (byte)(sumGray / count) : (byte)0;
                        int targetIndex = targetY * targetWidth + targetX;
                        _characterBuffer[targetIndex] = _lookupTable[avgGray];

                        if (includeColor)
                        {
                            byte avgR = count > 0 ? (byte)(sumR / count) : (byte)255;
                            byte avgG = count > 0 ? (byte)(sumG / count) : (byte)255;
                            byte avgB = count > 0 ? (byte)(sumB / count) : (byte)255;
                            _colorBuffer[targetIndex] = (avgR, avgG, avgB);
                        }
                    }
                });
            }
        }

        return GetReusableFrame(targetWidth, targetHeight, includeColor);
    }

    /// <summary>
    /// 直接从 RGB24 计算 BT.709 灰度并聚合字符单元格，避免先生成整帧灰度缓冲区。
    /// 计算顺序与“灰度转换后再映射”完全一致。
    /// </summary>
    public AsciiFrame MapRgb(
        byte[] rgbData,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        bool includeColor)
    {
        ValidateRgbParameters(rgbData, width, height, targetWidth, targetHeight);

        int cellCount = checked(targetWidth * targetHeight);
        if (_characterBuffer.Length != cellCount)
            _characterBuffer = new char[cellCount];
        if (includeColor && _colorBuffer.Length != cellCount)
            _colorBuffer = new (byte R, byte G, byte B)[cellCount];

        unsafe
        {
            fixed (byte* rgbPtr = rgbData)
            {
                IntPtr rgbAddress = (IntPtr)rgbPtr;
                if (includeColor)
                {
                    Parallel.For(0, targetHeight, _parallelOptions, targetY =>
                    {
                        byte* source = (byte*)rgbAddress;
                        int startY = targetY * height / targetHeight;
                        int endY = (targetY + 1) * height / targetHeight;

                        for (int targetX = 0; targetX < targetWidth; targetX++)
                        {
                            int startX = targetX * width / targetWidth;
                            int endX = (targetX + 1) * width / targetWidth;
                            long sumR = 0, sumG = 0, sumB = 0, sumGray = 0;
                            int count = 0;

                            for (int y = startY; y < endY; y++)
                            {
                                int rgbIndex = (y * width + startX) * 3;
                                for (int x = startX; x < endX; x++, rgbIndex += 3)
                                {
                                    byte red = source[rgbIndex];
                                    byte green = source[rgbIndex + 1];
                                    byte blue = source[rgbIndex + 2];
                                    sumR += red;
                                    sumG += green;
                                    sumB += blue;
                                    sumGray += (red * RedLuminanceCoefficient +
                                                green * GreenLuminanceCoefficient +
                                                blue * BlueLuminanceCoefficient) >> 8;
                                    count++;
                                }
                            }

                            int targetIndex = targetY * targetWidth + targetX;
                            byte averageGray = count > 0 ? (byte)(sumGray / count) : (byte)0;
                            _characterBuffer[targetIndex] = _lookupTable[averageGray];
                            _colorBuffer[targetIndex] = count > 0
                                ? ((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count))
                                : ((byte)255, (byte)255, (byte)255);
                        }
                    });
                }
                else
                {
                    Parallel.For(0, targetHeight, _parallelOptions, targetY =>
                    {
                        byte* source = (byte*)rgbAddress;
                        int startY = targetY * height / targetHeight;
                        int endY = (targetY + 1) * height / targetHeight;

                        for (int targetX = 0; targetX < targetWidth; targetX++)
                        {
                            int startX = targetX * width / targetWidth;
                            int endX = (targetX + 1) * width / targetWidth;
                            long sumGray = 0;
                            int count = 0;

                            for (int y = startY; y < endY; y++)
                            {
                                int rgbIndex = (y * width + startX) * 3;
                                for (int x = startX; x < endX; x++, rgbIndex += 3)
                                {
                                    byte red = source[rgbIndex];
                                    byte green = source[rgbIndex + 1];
                                    byte blue = source[rgbIndex + 2];
                                    sumGray += (red * RedLuminanceCoefficient +
                                                green * GreenLuminanceCoefficient +
                                                blue * BlueLuminanceCoefficient) >> 8;
                                    count++;
                                }
                            }

                            int targetIndex = targetY * targetWidth + targetX;
                            byte averageGray = count > 0 ? (byte)(sumGray / count) : (byte)0;
                            _characterBuffer[targetIndex] = _lookupTable[averageGray];
                        }
                    });
                }
            }
        }

        return GetReusableFrame(targetWidth, targetHeight, includeColor);
    }

    private AsciiFrame GetReusableFrame(int width, int height, bool includeColor)
    {
        AsciiFrame? cached = includeColor ? _colorFrame : _monochromeFrame;
        (byte R, byte G, byte B)[]? colors = includeColor ? _colorBuffer : null;
        if (cached is null ||
            cached.Width != width ||
            cached.Height != height ||
            !ReferenceEquals(cached.Characters, _characterBuffer) ||
            !ReferenceEquals(cached.Colors, colors))
        {
            cached = new AsciiFrame(width, height, _characterBuffer, colors);
            if (includeColor)
                _colorFrame = cached;
            else
                _monochromeFrame = cached;
        }

        return cached;
    }

    /// <summary>
    /// 参数验证
    /// </summary>
    private static void ValidateParameters(
        byte[] grayData,
        byte[]? rgbData,
        int width,
        int height,
        int targetWidth,
        int targetHeight)
    {
        if (grayData == null)
            throw new ArgumentNullException(nameof(grayData));

        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(
                $"Width and height must be positive: {width}x{height}");

        int pixelCount = checked(width * height);
        if (grayData.Length != pixelCount)
            throw new ArgumentException(
                $"Gray data length {grayData.Length} doesn't match {width}x{height}");

        if (targetWidth <= 0 || targetHeight <= 0 || targetWidth > width || targetHeight > height)
            throw new ArgumentOutOfRangeException(
                $"Target dimensions must be positive and not exceed the source: {targetWidth}x{targetHeight}");

        if (rgbData is not null && rgbData.Length != checked(pixelCount * 3))
            throw new ArgumentException(
                $"RGB data length {rgbData.Length} doesn't match {width}x{height}", nameof(rgbData));
    }

    private static void ValidateRgbParameters(
        byte[] rgbData,
        int width,
        int height,
        int targetWidth,
        int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(rgbData);

        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(
                $"Width and height must be positive: {width}x{height}");

        int pixelCount = checked(width * height);
        if (rgbData.Length != checked(pixelCount * 3))
            throw new ArgumentException(
                $"RGB data length {rgbData.Length} doesn't match {width}x{height}", nameof(rgbData));

        if (targetWidth <= 0 || targetHeight <= 0 || targetWidth > width || targetHeight > height)
            throw new ArgumentOutOfRangeException(
                $"Target dimensions must be positive and not exceed the source: {targetWidth}x{targetHeight}");
    }

    private static string ToAsciiString(AsciiFrame frame)
    {
        char[] output = new char[checked(frame.Characters.Length + frame.Height - 1)];
        int destination = 0;
        for (int y = 0; y < frame.Height; y++)
        {
            Array.Copy(frame.Characters, y * frame.Width, output, destination, frame.Width);
            destination += frame.Width;
            if (y + 1 < frame.Height)
                output[destination++] = '\n';
        }
        return new string(output);
    }
}
