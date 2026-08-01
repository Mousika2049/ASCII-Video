using System;
namespace AsciiFlow.Core.Processing;

/// <summary>
/// 使用行级并行和指针访问的灰度转换器。
/// 性能目标：~4ms/帧（1920x1080）
/// </summary>
public class ParallelGrayscaleConverter : IGrayscaleConverter
{
    // ITU-R BT.709 人眼感知亮度系数：0.2126 * R + 0.7152 * G + 0.0722 * B
    // 整数化： (54 * R + 183 * G + 19 * B) >> 8 (54 + 183 + 19 = 256)
    private const int R_COEFF = 54;
    private const int G_COEFF = 183;
    private const int B_COEFF = 19;
    private byte[] _grayBuffer = [];

    /// <summary>
    /// 将 RGB 数据转换为灰度数据（Parallel + Pointer 优化版本）
    /// </summary>
    public byte[] ConvertToGrayscale(byte[] rgbData, int width, int height)
    {
        if (rgbData == null)
            throw new ArgumentNullException(nameof(rgbData));

        if (width <= 0 || height <= 0)
            throw new ArgumentException($"Width 和 height 必须是正数，当前：{width}x{height}");

        int pixelCount = checked(width * height);
        int expectedLength = checked(pixelCount * 3);
        if (rgbData.Length != expectedLength)
            throw new ArgumentException(
                $"RGB 数据长度不匹配，期望 {expectedLength}，实际 {rgbData.Length}");

        if (_grayBuffer.Length != pixelCount)
            _grayBuffer = new byte[pixelCount];
        byte[] grayData = _grayBuffer;

        unsafe
        {
            fixed (byte* rgbPtr = rgbData)
            fixed (byte* grayPtr = grayData)
            {
                IntPtr rgbAddr = (IntPtr)rgbPtr;
                IntPtr grayAddr = (IntPtr)grayPtr;

                Parallel.For(0, height, y =>
                {
                    byte* rPtr = (byte*)rgbAddr;
                    byte* gPtr = (byte*)grayAddr;

                    int rowRgbOffset = y * width * 3;
                    int rowGrayOffset = y * width;

                    byte* rgbRow = rPtr + rowRgbOffset;
                    byte* grayRow = gPtr + rowGrayOffset;

                    for (int x = 0; x < width; x++)
                    {
                        int rgbIdx = x * 3;
                        byte r = rgbRow[rgbIdx];
                        byte g = rgbRow[rgbIdx + 1];
                        byte b = rgbRow[rgbIdx + 2];

                        grayRow[x] = (byte)((r * R_COEFF + g * G_COEFF + b * B_COEFF) >> 8);
                    }
                });
            }
        }

        return grayData;
    }
}

/// <summary>兼容旧名称；实现采用并行标量算法，并非 SIMD。</summary>
[Obsolete("请改用 ParallelGrayscaleConverter。旧名称不能准确描述实现。")]
public sealed class SimdGrayscaleConverter : ParallelGrayscaleConverter
{
}
