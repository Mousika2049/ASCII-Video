using AsciiFlow.Core.AsciiMapping;
using AsciiFlow.Core.Processing;
using AsciiFlow.Core.Rendering;
using AsciiFlow.Core.Video;

namespace AsciiFlow.Core.Tests;

public class AsciiPipelineTests
{
    [Fact]
    public void Mapper_RejectsInvalidRgbBufferBeforeUnsafeRead()
    {
        var mapper = new LookupTableAsciiMapper();
        byte[] grayscale = new byte[4];

        Assert.Throws<ArgumentException>(() =>
            mapper.Map(grayscale, 2, 2, 1, 1, new byte[3]));
    }

    [Fact]
    public void Mapper_MonochromePathDoesNotAllocateColors_AndReusesCharacters()
    {
        var mapper = new LookupTableAsciiMapper();
        byte[] grayscale = [0, 64, 128, 255];

        AsciiFrame first = mapper.Map(grayscale, 2, 2, 2, 2);
        AsciiFrame second = mapper.Map(grayscale, 2, 2, 2, 2);

        Assert.Null(first.Colors);
        Assert.Same(first.Characters, second.Characters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mapper_FusedRgbPathMatchesSeparateGrayscaleAndMapping(bool includeColor)
    {
        const int width = 17;
        const int height = 11;
        const int targetWidth = 7;
        const int targetHeight = 5;
        byte[] rgb = Enumerable.Range(0, width * height * 3)
            .Select(index => (byte)((index * 73 + 19) & 0xff))
            .ToArray();
        byte[] grayscale = new ParallelGrayscaleConverter().ConvertToGrayscale(rgb, width, height);
        var separateMapper = new LookupTableAsciiMapper();
        var fusedMapper = new LookupTableAsciiMapper();

        AsciiFrame expected = separateMapper.Map(
            grayscale,
            width,
            height,
            targetWidth,
            targetHeight,
            includeColor ? rgb : null);
        AsciiFrame actual = fusedMapper.MapRgb(
            rgb,
            width,
            height,
            targetWidth,
            targetHeight,
            includeColor);

        Assert.Equal(expected.Characters, actual.Characters);
        Assert.Equal(expected.Colors, actual.Colors);
    }

    [Fact]
    public void Mapper_FusedRgbPathReusesOutputBuffers()
    {
        var mapper = new LookupTableAsciiMapper();
        byte[] rgb = new byte[4 * 4 * 3];

        AsciiFrame first = mapper.MapRgb(rgb, 4, 4, 2, 2, includeColor: true);
        AsciiFrame second = mapper.MapRgb(rgb, 4, 4, 2, 2, includeColor: true);

        Assert.Same(first.Characters, second.Characters);
        Assert.Same(first.Colors, second.Colors);
        Assert.Same(first, second);
    }

    [Fact]
    public void Renderer_CoversNonDivisibleOutputDimensions()
    {
        var config = new CharacterSetConfig
        {
            FontFamily = "monospace",
            FontSize = 4,
            CharWidth = 4,
            CharHeight = 4
        };
        using var renderer = new SkiaCachedAsciiRenderer(config, 3, 2, 10, 7);
        renderer.Initialize();
        var frame = new AsciiFrame(
            3,
            2,
            Enumerable.Repeat('@', 6).ToArray(),
            Enumerable.Repeat(((byte)100, (byte)0, (byte)0), 6).ToArray());

        byte[] pixels = renderer.RenderFrame(frame, useColor: true);

        for (int pixel = 0; pixel < 10 * 7; pixel++)
            Assert.True(pixels[pixel * 3] > 0, $"Pixel {pixel} was left uncovered");
    }

    [Fact]
    public void Renderer_MonochromeExactCellFastPathMatchesStringRenderer()
    {
        var config = new CharacterSetConfig
        {
            FontFamily = "monospace",
            FontSize = 4,
            CharWidth = 4,
            CharHeight = 4
        };
        using var renderer = new SkiaCachedAsciiRenderer(config, 3, 2);
        renderer.Initialize();
        char[] characters = "@A z# ".ToCharArray();
        var frame = new AsciiFrame(3, 2, characters, null);

        byte[] structured = renderer.RenderFrame(frame, useColor: false).ToArray();
        byte[] legacy = renderer.RenderFrame("@A \nz#").ToArray();

        Assert.Equal(legacy, structured);
    }

    [Fact]
    public void Renderer_PrecomputedCoordinatesMatchLegacyNonDivisibleRendering()
    {
        var config = new CharacterSetConfig
        {
            FontFamily = "monospace",
            FontSize = 4,
            CharWidth = 4,
            CharHeight = 4
        };
        using var renderer = new SkiaCachedAsciiRenderer(config, 3, 2, 10, 7);
        renderer.Initialize();
        char[] characters = "@A z# ".ToCharArray();
        var frame = new AsciiFrame(3, 2, characters, null);

        byte[] structured = renderer.RenderFrame(frame, useColor: false).ToArray();
        byte[] legacy = renderer.RenderFrame("@A \nz# ").ToArray();

        Assert.Equal(legacy, structured);
    }

    [Fact]
    public void Renderer_PrecomputedCoordinatesMatchLegacyColorRendering()
    {
        var config = new CharacterSetConfig
        {
            FontFamily = "monospace",
            FontSize = 4,
            CharWidth = 4,
            CharHeight = 4
        };
        using var renderer = new SkiaCachedAsciiRenderer(config, 3, 2, 10, 7);
        renderer.Initialize();
        char[] characters = "@A z# ".ToCharArray();
        (byte R, byte G, byte B)[] colors =
        [
            (255, 0, 0),
            (0, 255, 0),
            (0, 0, 255),
            (255, 255, 0),
            (0, 255, 255),
            (255, 0, 255)
        ];
        var frame = new AsciiFrame(3, 2, characters, colors);

        byte[] structured = renderer.RenderFrame(frame, useColor: true).ToArray();
        byte[] legacy = renderer.RenderFrameWithColor("@A \nz# ", colors).ToArray();

        Assert.Equal(legacy, structured);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Renderer_DirectYuv420pMatchesRgbReferenceConversion(bool useColor)
    {
        var config = new CharacterSetConfig
        {
            FontFamily = "monospace",
            FontSize = 4,
            CharWidth = 4,
            CharHeight = 4
        };
        using var renderer = new SkiaCachedAsciiRenderer(config, 3, 2, 10, 8);
        renderer.Initialize();
        var frame = new AsciiFrame(
            3,
            2,
            "@A z# ".ToCharArray(),
            new (byte R, byte G, byte B)[]
            {
                (255, 0, 0),
                (0, 255, 0),
                (0, 0, 255),
                (255, 255, 0),
                (0, 255, 255),
                (255, 0, 255)
            });

        byte[] rgb = renderer.RenderFrame(frame, useColor).ToArray();
        byte[] expected = ConvertRgbToYuv420p(rgb, renderer.OutputWidth, renderer.OutputHeight);
        byte[] actual = new byte[Yuv420pBuffer.GetSize(renderer.OutputWidth, renderer.OutputHeight)];

        renderer.RenderFrameYuv420p(frame, actual, useColor);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Renderer_DirectYuv420pDoesNotAllocateFullRgbFrameBuffer()
    {
        var config = new CharacterSetConfig
        {
            FontFamily = "monospace",
            FontSize = 4,
            CharWidth = 4,
            CharHeight = 4
        };
        using var renderer = new SkiaCachedAsciiRenderer(config, 2, 2, 8, 8);
        renderer.Initialize();
        var frame = new AsciiFrame(2, 2, "@@@@".ToCharArray(), null);
        var yuv = new byte[Yuv420pBuffer.GetSize(8, 8)];

        renderer.RenderFrameYuv420p(frame, yuv, useColor: false);

        Assert.False(renderer.HasRgbFrameBuffer);
    }

    private static byte[] ConvertRgbToYuv420p(byte[] rgb, int width, int height)
    {
        var yuv = new byte[Yuv420pBuffer.GetSize(width, height)];
        int lumaSize = width * height;
        int chromaWidth = width / 2;
        int uOffset = lumaSize;
        int vOffset = uOffset + lumaSize / 4;

        for (int y = 0; y < height; y += 2)
        {
            for (int x = 0; x < width; x += 2)
            {
                int redSum = 0;
                int greenSum = 0;
                int blueSum = 0;
                for (int row = 0; row < 2; row++)
                {
                    for (int column = 0; column < 2; column++)
                    {
                        int pixel = (y + row) * width + x + column;
                        int rgbOffset = pixel * 3;
                        int red = rgb[rgbOffset];
                        int green = rgb[rgbOffset + 1];
                        int blue = rgb[rgbOffset + 2];
                        redSum += red;
                        greenSum += green;
                        blueSum += blue;
                        yuv[pixel] = Clamp(
                            ((47 * red + 157 * green + 16 * blue + 128) >> 8) + 16);
                    }
                }

                int averageRed = (redSum + 2) >> 2;
                int averageGreen = (greenSum + 2) >> 2;
                int averageBlue = (blueSum + 2) >> 2;
                int chromaIndex = (y / 2) * chromaWidth + x / 2;
                yuv[uOffset + chromaIndex] = Clamp(
                    ((-26 * averageRed - 87 * averageGreen + 112 * averageBlue + 128) >> 8) + 128);
                yuv[vOffset + chromaIndex] = Clamp(
                    ((112 * averageRed - 102 * averageGreen - 10 * averageBlue + 128) >> 8) + 128);
            }
        }

        return yuv;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
