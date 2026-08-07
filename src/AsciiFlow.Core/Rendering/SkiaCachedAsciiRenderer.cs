using System.Runtime.CompilerServices;
using AsciiFlow.Core.AsciiMapping;
using AsciiFlow.Core.Video;
using SkiaSharp;

namespace AsciiFlow.Core.Rendering;

/// <summary>
/// 基于 SkiaSharp 3.x + 字符位图缓存的高性能 ASCII 渲染器
/// 性能目标：≤ 0.5ms/帧（1080p → 80x40）
/// </summary>
public class SkiaCachedAsciiRenderer : IAsciiRenderer, IYuv420pAsciiRenderer
{
    private readonly CharacterSetConfig _config;
    private readonly int _targetWidth;
    private readonly int _targetHeight;
    private readonly int[] _cellXBoundaries;
    private readonly int[] _cellYBoundaries;
    private readonly int[] _cellXByOutputX;
    private readonly int[] _cellYByOutputY;
    private readonly int[] _sourceXByOutputX;
    private readonly int[] _sourceRowOffsetByOutputY;
    private readonly int[] _cellBackgroundRgb;
    private readonly int[] _cellStrokeRgb;

    // 字符缓存：256 个 ASCII 字符的 RGB24 位图数据与 Alpha 遮罩数据
    private byte[][] _charBitmaps = new byte[256][];
    private byte[][] _charAlphaMasks = new byte[256][];

    // 预分配的 RGB24 输出缓冲区
    private byte[] _rgbBuffer = [];

    private bool _initialized;
    private bool _disposed;

    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }
    public int CharWidth => _config.CharWidth;
    public int CharHeight => _config.CharHeight;
    internal bool HasRgbFrameBuffer => _rgbBuffer.Length > 0;

    /// <summary>
    /// 构造函数
    /// </summary>
    public SkiaCachedAsciiRenderer(
        CharacterSetConfig config,
        int targetWidth,
        int targetHeight) : this(config, targetWidth, targetHeight, targetWidth * config.CharWidth, targetHeight * config.CharHeight)
    {
    }

    /// <summary>
    /// 构造函数（指定输出视频像素分辨率）
    /// </summary>
    public SkiaCachedAsciiRenderer(
        CharacterSetConfig config,
        int targetWidth,
        int targetHeight,
        int outputWidth,
        int outputHeight)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;

        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        if (targetWidth <= 0 || targetHeight <= 0 || outputWidth <= 0 || outputHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "网格和输出尺寸必须为正数");
        int cellCount = checked(targetWidth * targetHeight);
        _cellBackgroundRgb = new int[cellCount];
        _cellStrokeRgb = new int[cellCount];
        _cellXBoundaries = BuildCellBoundaries(targetWidth, outputWidth);
        _cellYBoundaries = BuildCellBoundaries(targetHeight, outputHeight);
        _cellXByOutputX = BuildCellIndexMap(_cellXBoundaries, outputWidth);
        _cellYByOutputY = BuildCellIndexMap(_cellYBoundaries, outputHeight);
        _sourceXByOutputX = BuildSourceCoordinateMap(
            _cellXBoundaries,
            config.CharWidth,
            outputWidth);
        int[] sourceYByOutputY = BuildSourceCoordinateMap(
            _cellYBoundaries,
            config.CharHeight,
            outputHeight);
        _sourceRowOffsetByOutputY = new int[sourceYByOutputY.Length];
        for (int outputY = 0; outputY < sourceYByOutputY.Length; outputY++)
            _sourceRowOffsetByOutputY[outputY] = sourceYByOutputY[outputY] * config.CharWidth;
    }

    /// <summary>
    /// 初始化渲染器，预渲染 256 个 ASCII 字符到缓存
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;

        // 使用 SkiaSharp 3.x 新 API：SKFont + SKPaint 分离
        using var font = CreateSkFont();
        using var paint = CreateSkPaint();

        for (int i = 0; i < 256; i++)
        {
            var (rgb, alpha) = RenderCharToBitmap((char)i, font, paint);
            _charBitmaps[i] = rgb;
            _charAlphaMasks[i] = alpha;
        }

        _initialized = true;
    }

    /// <summary>
    /// 创建 SKFont 对象（SkiaSharp 3.x 新 API：字体属性从 SKPaint 移到 SKFont）
    /// </summary>
    private SKFont CreateSkFont()
    {
        SKTypeface? typeface = null;

        // 候选字体列表（跨平台兼容：Windows / Linux / macOS）
        string[] candidates = new string[]
        {
            _config.FontFamily,
            "Consolas",
            "Cascadia Mono",
            "Cascadia Code",
            "DejaVu Sans Mono",
            "Liberation Mono",
            "FreeMono",
            "Courier New",
            "Courier"
        };

        var availableFamilies = SKFontManager.Default.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            if (availableFamilies.Contains(candidate))
            {
                var tf = SKTypeface.FromFamilyName(candidate, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                if (tf != null && !string.IsNullOrEmpty(tf.FamilyName))
                {
                    typeface = tf;
                    break;
                }
            }
        }

        if (typeface == null || string.IsNullOrEmpty(typeface.FamilyName))
        {
            typeface = SKFontManager.Default.MatchFamily("monospace", SKFontStyle.Normal)
                       ?? SKTypeface.Default;
        }

        return new SKFont(typeface, _config.FontSize)
        {
            Edging = SKFontEdging.SubpixelAntialias,
            Hinting = SKFontHinting.None
        };
    }

    /// <summary>
    /// 创建 SKPaint 对象（仅设置颜色等效果属性）
    /// </summary>
    private SKPaint CreateSkPaint()
    {
        return new SKPaint
        {
            Color = new SKColor(
                _config.ForegroundColor.R,
                _config.ForegroundColor.G,
                _config.ForegroundColor.B),
            IsAntialias = false,  // 关闭抗锯齿以提升字符画清晰度
            Style = SKPaintStyle.Fill
        };
    }

    /// <summary>
    /// 直接渲染结构化 ASCII 帧。按比例计算单元格边界，确保覆盖完整输出画面。
    /// </summary>
    public byte[] RenderFrame(AsciiFrame frame, bool useColor = true)
    {
        if (!_initialized)
            throw new InvalidOperationException("渲染器未初始化，请先调用 Initialize()");
        ValidateFrame(frame, ref useColor);
        EnsureRgbFrameBuffer();

        int outputWidth = OutputWidth;
        int rowStride = outputWidth * 3;
        int charWidth = CharWidth;
        int charHeight = CharHeight;

        unsafe
        {
            fixed (byte* bufferPointer = _rgbBuffer)
            {
                IntPtr bufferAddress = (IntPtr)bufferPointer;
                Parallel.For(0, frame.Height, targetY =>
                {
                    byte* destination = (byte*)bufferAddress;
                    int y0 = _cellYBoundaries[targetY];
                    int y1 = _cellYBoundaries[targetY + 1];
                    int cellHeight = y1 - y0;

                    for (int targetX = 0; targetX < frame.Width; targetX++)
                    {
                        int cellIndex = targetY * frame.Width + targetX;
                        int characterCode = frame.Characters[cellIndex] < 256
                            ? frame.Characters[cellIndex]
                            : 32;
                        int x0 = _cellXBoundaries[targetX];
                        int x1 = _cellXBoundaries[targetX + 1];
                        int cellWidth = x1 - x0;
                        if (cellWidth <= 0 || cellHeight <= 0) continue;

                        byte[] bitmap = _charBitmaps[characterCode];
                        byte[] alphaMask = _charAlphaMasks[characterCode];

                        if (!useColor && cellWidth == charWidth && cellHeight == charHeight)
                        {
                            fixed (byte* source = bitmap)
                            {
                                long rowBytes = charWidth * 3L;
                                for (int row = 0; row < charHeight; row++)
                                {
                                    byte* sourceRow = source + row * charWidth * 3;
                                    byte* destinationRow = destination + (y0 + row) * rowStride + x0 * 3;
                                    Buffer.MemoryCopy(sourceRow, destinationRow, rowBytes, rowBytes);
                                }
                            }
                            continue;
                        }

                        (byte R, byte G, byte B) foreground = useColor
                            ? frame.Colors![cellIndex]
                            : _config.ForegroundColor;
                        (byte R, byte G, byte B) background = useColor
                            ? ((byte)(foreground.R * 0.15f), (byte)(foreground.G * 0.15f), (byte)(foreground.B * 0.15f))
                            : _config.BackgroundColor;
                        byte strokeR = useColor ? (byte)Math.Min(255, (int)(foreground.R * 1.25f)) : foreground.R;
                        byte strokeG = useColor ? (byte)Math.Min(255, (int)(foreground.G * 1.25f)) : foreground.G;
                        byte strokeB = useColor ? (byte)Math.Min(255, (int)(foreground.B * 1.25f)) : foreground.B;

                        for (int outputY = y0; outputY < y1; outputY++)
                        {
                            int sourceRowOffset = _sourceRowOffsetByOutputY[outputY];
                            int outputRow = outputY * rowStride;
                            for (int outputX = x0; outputX < x1; outputX++)
                            {
                                int sourcePixel = sourceRowOffset + _sourceXByOutputX[outputX];
                                byte* pixel = destination + outputRow + outputX * 3;

                                if (!useColor)
                                {
                                    int bitmapOffset = sourcePixel * 3;
                                    pixel[0] = bitmap[bitmapOffset];
                                    pixel[1] = bitmap[bitmapOffset + 1];
                                    pixel[2] = bitmap[bitmapOffset + 2];
                                    continue;
                                }

                                byte alpha = alphaMask[sourcePixel];
                                if (alpha == 0)
                                {
                                    pixel[0] = background.R;
                                    pixel[1] = background.G;
                                    pixel[2] = background.B;
                                    continue;
                                }
                                if (alpha == 255)
                                {
                                    pixel[0] = strokeR;
                                    pixel[1] = strokeG;
                                    pixel[2] = strokeB;
                                    continue;
                                }

                                int inverseAlpha = 255 - alpha;
                                pixel[0] = (byte)((strokeR * alpha + background.R * inverseAlpha) / 255);
                                pixel[1] = (byte)((strokeG * alpha + background.G * inverseAlpha) / 255);
                                pixel[2] = (byte)((strokeB * alpha + background.B * inverseAlpha) / 255);
                            }
                        }
                    }
                });
            }
        }

        return _rgbBuffer;
    }

    /// <summary>
    /// 直接生成有限范围 BT.709 YUV420P，不构造中间 RGB24 输出帧。
    /// </summary>
    public void RenderFrameYuv420p(AsciiFrame frame, byte[] destination, bool useColor = true)
    {
        if (!_initialized)
            throw new InvalidOperationException("渲染器未初始化，请先调用 Initialize()");
        ValidateFrame(frame, ref useColor);
        Yuv420pBuffer.Validate(destination, OutputWidth, OutputHeight, nameof(destination));
        if (useColor)
            PrepareCellColors(frame.Colors!);

        int outputWidth = OutputWidth;
        int outputHeight = OutputHeight;
        int lumaSize = outputWidth * outputHeight;
        int chromaWidth = outputWidth / 2;
        int uOffset = lumaSize;
        int vOffset = uOffset + lumaSize / 4;

        Parallel.For(0, outputHeight / 2, chromaY =>
        {
            int outputY = chromaY * 2;
            int firstLumaRow = outputY * outputWidth;
            int secondLumaRow = firstLumaRow + outputWidth;
            int chromaRow = chromaY * chromaWidth;

            for (int chromaX = 0; chromaX < chromaWidth; chromaX++)
            {
                int outputX = chromaX * 2;
                int topLeft = GetRenderedRgb(frame, useColor, outputX, outputY);
                int topRight = GetRenderedRgb(frame, useColor, outputX + 1, outputY);
                int bottomLeft = GetRenderedRgb(frame, useColor, outputX, outputY + 1);
                int bottomRight = GetRenderedRgb(frame, useColor, outputX + 1, outputY + 1);

                destination[firstLumaRow + outputX] = RgbToLuma(topLeft);
                destination[firstLumaRow + outputX + 1] = RgbToLuma(topRight);
                destination[secondLumaRow + outputX] = RgbToLuma(bottomLeft);
                destination[secondLumaRow + outputX + 1] = RgbToLuma(bottomRight);

                int averageRed =
                    (GetRed(topLeft) + GetRed(topRight) + GetRed(bottomLeft) + GetRed(bottomRight) + 2) >> 2;
                int averageGreen =
                    (GetGreen(topLeft) + GetGreen(topRight) + GetGreen(bottomLeft) + GetGreen(bottomRight) + 2) >> 2;
                int averageBlue =
                    (GetBlue(topLeft) + GetBlue(topRight) + GetBlue(bottomLeft) + GetBlue(bottomRight) + 2) >> 2;
                int chromaIndex = chromaRow + chromaX;
                destination[uOffset + chromaIndex] = RgbToChromaU(averageRed, averageGreen, averageBlue);
                destination[vOffset + chromaIndex] = RgbToChromaV(averageRed, averageGreen, averageBlue);
            }
        });
    }

    private void ValidateFrame(AsciiFrame frame, ref bool useColor)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width != _targetWidth || frame.Height != _targetHeight)
            throw new ArgumentException(
                $"ASCII 帧尺寸 {frame.Width}x{frame.Height} 与渲染器 {_targetWidth}x{_targetHeight} 不匹配",
                nameof(frame));
        if (frame.Characters.Length != checked(frame.Width * frame.Height))
            throw new ArgumentException("ASCII 字符数组长度不正确", nameof(frame));
        if (!useColor)
            return;
        if (frame.Colors is null)
            useColor = false;
        else if (frame.Colors.Length != frame.Characters.Length)
            throw new ArgumentException("ASCII 颜色数组长度不正确", nameof(frame));
    }

    private void EnsureRgbFrameBuffer()
    {
        int expectedLength = checked(OutputWidth * OutputHeight * 3);
        if (_rgbBuffer.Length != expectedLength)
            _rgbBuffer = new byte[expectedLength];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetRenderedRgb(AsciiFrame frame, bool useColor, int outputX, int outputY)
    {
        int cellIndex = _cellYByOutputY[outputY] * frame.Width + _cellXByOutputX[outputX];
        int characterCode = frame.Characters[cellIndex] < 256
            ? frame.Characters[cellIndex]
            : 32;
        int sourcePixel = _sourceRowOffsetByOutputY[outputY] + _sourceXByOutputX[outputX];

        if (!useColor)
        {
            int bitmapOffset = sourcePixel * 3;
            byte[] bitmap = _charBitmaps[characterCode];
            return PackRgb(bitmap[bitmapOffset], bitmap[bitmapOffset + 1], bitmap[bitmapOffset + 2]);
        }

        int background = _cellBackgroundRgb[cellIndex];
        byte alpha = _charAlphaMasks[characterCode][sourcePixel];
        if (alpha == 0)
            return background;

        int stroke = _cellStrokeRgb[cellIndex];
        if (alpha == 255)
            return stroke;

        int inverseAlpha = 255 - alpha;
        return PackRgb(
            (GetRed(stroke) * alpha + GetRed(background) * inverseAlpha) / 255,
            (GetGreen(stroke) * alpha + GetGreen(background) * inverseAlpha) / 255,
            (GetBlue(stroke) * alpha + GetBlue(background) * inverseAlpha) / 255);
    }

    private void PrepareCellColors((byte R, byte G, byte B)[] colors)
    {
        Parallel.For(0, colors.Length, cellIndex =>
        {
            (byte red, byte green, byte blue) = colors[cellIndex];
            _cellBackgroundRgb[cellIndex] = PackRgb(
                (byte)(red * 0.15f),
                (byte)(green * 0.15f),
                (byte)(blue * 0.15f));
            _cellStrokeRgb[cellIndex] = PackRgb(
                Math.Min(255, (int)(red * 1.25f)),
                Math.Min(255, (int)(green * 1.25f)),
                Math.Min(255, (int)(blue * 1.25f)));
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PackRgb(int red, int green, int blue) => (red << 16) | (green << 8) | blue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetRed(int rgb) => (rgb >> 16) & 0xff;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetGreen(int rgb) => (rgb >> 8) & 0xff;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBlue(int rgb) => rgb & 0xff;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte RgbToLuma(int rgb) => ClampToByte(
        ((47 * GetRed(rgb) + 157 * GetGreen(rgb) + 16 * GetBlue(rgb) + 128) >> 8) + 16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte RgbToChromaU(int red, int green, int blue) =>
        ClampToByte(((-26 * red - 87 * green + 112 * blue + 128) >> 8) + 128);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte RgbToChromaV(int red, int green, int blue) =>
        ClampToByte(((112 * red - 102 * green - 10 * blue + 128) >> 8) + 128);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampToByte(int value) => (byte)Math.Clamp(value, 0, 255);

    private static int[] BuildCellBoundaries(int cellCount, int outputSize)
    {
        var boundaries = new int[cellCount + 1];
        for (int index = 0; index <= cellCount; index++)
            boundaries[index] = (int)((long)index * outputSize / cellCount);
        return boundaries;
    }

    private static int[] BuildCellIndexMap(int[] cellBoundaries, int outputSize)
    {
        var cells = new int[outputSize];
        for (int cell = 0; cell + 1 < cellBoundaries.Length; cell++)
        {
            for (int output = cellBoundaries[cell]; output < cellBoundaries[cell + 1]; output++)
                cells[output] = cell;
        }

        return cells;
    }

    private static int[] BuildSourceCoordinateMap(
        int[] cellBoundaries,
        int sourceCellSize,
        int outputSize)
    {
        var coordinates = new int[outputSize];
        for (int cell = 0; cell + 1 < cellBoundaries.Length; cell++)
        {
            int start = cellBoundaries[cell];
            int end = cellBoundaries[cell + 1];
            int destinationCellSize = end - start;
            if (destinationCellSize <= 0)
                continue;

            for (int output = start; output < end; output++)
            {
                coordinates[output] = Math.Min(
                    sourceCellSize - 1,
                    (output - start) * sourceCellSize / destinationCellSize);
            }
        }

        return coordinates;
    }

    /// <summary>
    /// 将单个字符渲染为 RGB24 位图数据与 Alpha 遮罩数据
    /// </summary>
    private (byte[] Rgb, byte[] Alpha) RenderCharToBitmap(char c, SKFont font, SKPaint paint)
    {
        var bg = _config.BackgroundColor;
        var fg = _config.ForegroundColor;
        int cw = CharWidth;
        int ch = CharHeight;

        // 1. 创建临时 RGBA 位图（SkiaSharp 使用 BGRA 内存布局）
        using var bitmap = new SKBitmap(cw, ch, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            // 2. 填充背景
            canvas.Clear(new SKColor(bg.R, bg.G, bg.B));

            // 3. 绘制字符（仅可打印 ASCII 范围 33-126）
            if (c > 32 && c < 127)
            {
                string charStr = c.ToString();

                // 使用 SKFont 测量字符边界
                var bounds = new SKRect();
                font.MeasureText(charStr, out bounds);

                // 垂直居中计算：使用 font.Metrics 计算 baseline 避免文本超出 16px 底部
                var metrics = font.Metrics;
                float fontHeight = metrics.Descent - metrics.Ascent;
                float x = (cw - bounds.Width) * 0.5f - bounds.Left;
                float y = (ch - fontHeight) * 0.5f - metrics.Ascent;

                canvas.DrawText(charStr, x, y, SKTextAlign.Left, font, paint);
            }

            canvas.Flush();
        }

        // 4. BGRA → RGB24 及 Alpha 遮罩提取
        byte[] rgbData = new byte[cw * ch * 3];
        byte[] alphaData = new byte[cw * ch];
        IntPtr pixelsAddr = bitmap.GetPixels();

        unsafe
        {
            byte* srcPtr = (byte*)pixelsAddr;
            int srcStride = bitmap.RowBytes;

            fixed (byte* dstPtr = rgbData)
            {
                byte* dst = dstPtr;
                for (int y = 0; y < ch; y++)
                {
                    byte* srcRow = srcPtr + y * srcStride;
                    for (int x = 0; x < cw; x++)
                    {
                        int pixelOffset = x * 4;
                        byte r = srcRow[pixelOffset + 2];
                        byte g = srcRow[pixelOffset + 1];
                        byte b = srcRow[pixelOffset];
                        *dst++ = r;
                        *dst++ = g;
                        *dst++ = b;

                        byte a = Math.Max(r, Math.Max(g, b));
                        alphaData[y * cw + x] = a;
                    }
                }
            }
        }

        return (rgbData, alphaData);
    }

    /// <summary>
    /// 将 ASCII 字符串渲染为 RGB24 字节数组（黑白模式）
    /// </summary>
    public byte[] RenderFrame(string asciiArt)
    {
        if (!_initialized)
            throw new InvalidOperationException("渲染器未初始化，请先调用 Initialize()");
        EnsureRgbFrameBuffer();

        if (string.IsNullOrEmpty(asciiArt))
        {
            Array.Clear(_rgbBuffer);
            return _rgbBuffer;
        }

        Array.Clear(_rgbBuffer);

        int ow = OutputWidth;
        int oh = OutputHeight;
        int rowStride = ow * 3;

        ReadOnlySpan<char> ascii = asciiArt.AsSpan();
        int lineIndex = 0;
        int lineStart = 0;
        int charW = CharWidth;
        int charH = CharHeight;

        for (int i = 0; i <= ascii.Length; i++)
        {
            bool isLineEnd = (i == ascii.Length) || (ascii[i] == '\n');
            if (!isLineEnd) continue;

            if (lineIndex >= _targetHeight) break;

            var lineSpan = ascii[lineStart..i];
            int linePixelY = lineIndex * oh / _targetHeight;
            int linePixelEndY = (lineIndex + 1) * oh / _targetHeight;
            if (linePixelY >= oh || linePixelEndY <= linePixelY) break;

            int maxChars = Math.Min(lineSpan.Length, _targetWidth);
            for (int charIdx = 0; charIdx < maxChars; charIdx++)
            {
                char ch = lineSpan[charIdx];
                int charCode = ch < 256 ? ch : 32;

                byte[] charBitmap = _charBitmaps[charCode];

                int destPixelX = charIdx * ow / _targetWidth;
                int destPixelEndX = (charIdx + 1) * ow / _targetWidth;
                int cellWidth = destPixelEndX - destPixelX;
                int cellHeight = linePixelEndY - linePixelY;
                if (cellWidth <= 0) continue;

                for (int destY = linePixelY; destY < linePixelEndY; destY++)
                {
                    int sourceY = Math.Min(charH - 1, (destY - linePixelY) * charH / cellHeight);
                    for (int destX = destPixelX; destX < destPixelEndX; destX++)
                    {
                        int sourceX = Math.Min(charW - 1, (destX - destPixelX) * charW / cellWidth);
                        int sourceOffset = (sourceY * charW + sourceX) * 3;
                        int destinationOffset = destY * rowStride + destX * 3;
                        _rgbBuffer[destinationOffset] = charBitmap[sourceOffset];
                        _rgbBuffer[destinationOffset + 1] = charBitmap[sourceOffset + 1];
                        _rgbBuffer[destinationOffset + 2] = charBitmap[sourceOffset + 2];
                    }
                }
            }

            lineIndex++;
            lineStart = i + 1;
        }

        return _rgbBuffer;
    }

    /// <summary>
    /// 将 ASCII 字符串渲染为 RGB24 字节数组（支持彩色字符，Parallel 并行加速 + 严苛内存边界保护）
    /// </summary>
    public byte[] RenderFrameWithColor(string asciiArt, (byte R, byte G, byte B)[] colors, bool useColor = true)
    {
        if (!_initialized)
            throw new InvalidOperationException("渲染器未初始化，请先调用 Initialize()");
        EnsureRgbFrameBuffer();

        if (!useColor || colors == null || colors.Length == 0)
            return RenderFrame(asciiArt);

        if (string.IsNullOrEmpty(asciiArt))
        {
            Array.Clear(_rgbBuffer);
            return _rgbBuffer;
        }

        Array.Clear(_rgbBuffer);

        int ow = OutputWidth;
        int oh = OutputHeight;
        int rowStride = ow * 3;
        var bg = _config.BackgroundColor;
        int charW = CharWidth;
        int charH = CharHeight;
        int targetW = _targetWidth;
        int targetH = _targetHeight;

        string[] lines = asciiArt.Split('\n');
        int lineCount = Math.Min(lines.Length, targetH);

        unsafe
        {
            fixed (byte* bufPtr = _rgbBuffer)
            {
                IntPtr bufAddr = (IntPtr)bufPtr;

                Parallel.For(0, lineCount, lineIndex =>
                {
                    byte* bPtr = (byte*)bufAddr;
                    string lineStr = lines[lineIndex];
                    int linePixelY = lineIndex * oh / targetH;
                    int linePixelEndY = (lineIndex + 1) * oh / targetH;
                    if (linePixelY < oh && linePixelEndY > linePixelY)
                    {
                        int colorOffsetBase = lineIndex * targetW;
                        int lineLen = Math.Min(lineStr.Length, targetW);

                        for (int charIdx = 0; charIdx < lineLen; charIdx++)
                        {
                            int destPixelX = charIdx * ow / targetW;
                            int destPixelEndX = (charIdx + 1) * ow / targetW;
                            int cellWidth = destPixelEndX - destPixelX;
                            int cellHeight = linePixelEndY - linePixelY;
                            if (cellWidth <= 0) continue;

                            char ch = lineStr[charIdx];
                            int charCode = ch < 256 ? ch : 32;

                            byte[] charAlpha = _charAlphaMasks[charCode];

                            int colorIdx = colorOffsetBase + charIdx;
                            var fgColor = colorIdx < colors.Length ? colors[colorIdx] : (bg.R, bg.G, bg.B);

                            // 【亮度补偿】由于字符笔画只覆盖单元格的一部分区域（如30%），纯黑背景会导致整体画面偏暗。
                            // 笔画亮度增益(1.25x) + 单元格底色衬托(15%原色)，缓解纯黑背景造成的整体偏暗。
                            byte bgR = (byte)(fgColor.R * 0.15f);
                            byte bgG = (byte)(fgColor.G * 0.15f);
                            byte bgB = (byte)(fgColor.B * 0.15f);

                            byte strokeR = (byte)Math.Min(255, (int)(fgColor.R * 1.25f));
                            byte strokeG = (byte)Math.Min(255, (int)(fgColor.G * 1.25f));
                            byte strokeB = (byte)Math.Min(255, (int)(fgColor.B * 1.25f));

                            for (int pxY = linePixelY; pxY < linePixelEndY; pxY++)
                            {
                                int rowOffset = pxY * rowStride;
                                int sourceY = Math.Min(charH - 1, (pxY - linePixelY) * charH / cellHeight);

                                for (int pxX = destPixelX; pxX < destPixelEndX; pxX++)
                                {
                                    int sourceX = Math.Min(charW - 1, (pxX - destPixelX) * charW / cellWidth);
                                    byte alpha = charAlpha[sourceY * charW + sourceX];
                                    byte* pxPtr = bPtr + rowOffset + pxX * 3;

                                    if (alpha == 0)
                                    {
                                        pxPtr[0] = bgR;
                                        pxPtr[1] = bgG;
                                        pxPtr[2] = bgB;
                                    }
                                    else if (alpha == 255)
                                    {
                                        pxPtr[0] = strokeR;
                                        pxPtr[1] = strokeG;
                                        pxPtr[2] = strokeB;
                                    }
                                    else
                                    {
                                        int invAlpha = 255 - alpha;
                                        pxPtr[0] = (byte)((strokeR * alpha + bgR * invAlpha) / 255);
                                        pxPtr[1] = (byte)((strokeG * alpha + bgG * invAlpha) / 255);
                                        pxPtr[2] = (byte)((strokeB * alpha + bgB * invAlpha) / 255);
                                    }
                                }
                            }
                        }
                    }
                });
            }
        }

        return _rgbBuffer;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _charBitmaps = null!;
        _charAlphaMasks = null!;
        _rgbBuffer = null!;
        _disposed = true;
    }
}
