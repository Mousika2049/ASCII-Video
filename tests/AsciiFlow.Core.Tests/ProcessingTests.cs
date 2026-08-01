using AsciiFlow.Core.Processing;

namespace AsciiFlow.Core.Tests;

public class ProcessingTests
{
    [Fact]
    public void ParallelConverter_MatchesScalarConverter_AndReusesBuffer()
    {
        byte[] rgb = Enumerable.Range(0, 8 * 6 * 3).Select(index => (byte)(index * 17)).ToArray();
        var scalar = new ScalarGrayscaleConverter();
        var parallel = new ParallelGrayscaleConverter();

        byte[] expected = scalar.ConvertToGrayscale(rgb, 8, 6);
        byte[] first = parallel.ConvertToGrayscale(rgb, 8, 6);
        byte[] second = parallel.ConvertToGrayscale(rgb, 8, 6);

        Assert.Equal(expected, first);
        Assert.Same(first, second);
    }
}
