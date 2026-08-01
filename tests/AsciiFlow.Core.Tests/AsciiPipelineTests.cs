using AsciiFlow.Core.AsciiMapping;
using AsciiFlow.Core.Processing;
using AsciiFlow.Core.Rendering;

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
}
