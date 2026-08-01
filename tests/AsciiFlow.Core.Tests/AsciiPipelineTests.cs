using AsciiFlow.Core.AsciiMapping;
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
}
