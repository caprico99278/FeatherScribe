using System.Globalization;
using System.Windows;
using FeatherScribe.App;

namespace FeatherScribe.Tests;

public class PillCornerRadiusConverterTests
{
    private static readonly PillCornerRadiusConverter Converter = new();

    [Theory]
    [InlineData(40.0, 20.0)]
    [InlineData(23.0, 11.5)]
    public void Convert_ReturnsHalfTheHeight(double height, double expectedRadius)
    {
        var result = (CornerRadius)Converter.Convert(height, typeof(CornerRadius), new CornerRadius(999), CultureInfo.InvariantCulture);

        Assert.Equal(new CornerRadius(expectedRadius), result);
    }

    [Fact]
    public void Convert_CapsTheRadiusWithTheTokenParameter()
    {
        var result = (CornerRadius)Converter.Convert(100.0, typeof(CornerRadius), new CornerRadius(8), CultureInfo.InvariantCulture);

        Assert.Equal(new CornerRadius(8), result);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Convert_UnmeasuredHeight_ReturnsZeroRadius(double height)
    {
        var result = (CornerRadius)Converter.Convert(height, typeof(CornerRadius), new CornerRadius(999), CultureInfo.InvariantCulture);

        Assert.Equal(new CornerRadius(0), result);
    }

    [Fact]
    public void Convert_WithoutParameter_UsesHalfTheHeight()
    {
        var result = (CornerRadius)Converter.Convert(30.0, typeof(CornerRadius), null!, CultureInfo.InvariantCulture);

        Assert.Equal(new CornerRadius(15), result);
    }
}
