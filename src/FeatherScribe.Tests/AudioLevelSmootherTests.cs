using FeatherScribe.App;

namespace FeatherScribe.Tests;

public sealed class AudioLevelSmootherTests
{
    [Fact]
    public void StartsAtZero_AndUsesSpecifiedCoefficients()
    {
        var smoother = new AudioLevelSmoother();

        Assert.Equal(0, smoother.Value);
        Assert.Equal(0.6, AudioLevelSmoother.Attack);
        Assert.Equal(0.25, AudioLevelSmoother.Release);
    }

    [Fact]
    public void RisesFasterThanItFalls()
    {
        var smoother = new AudioLevelSmoother();

        var rise = smoother.Next(1) - 0;
        var peak = smoother.Value;
        var fall = peak - smoother.Next(0);

        Assert.Equal(0.6, rise, precision: 10);
        Assert.Equal(0.15, fall, precision: 10);
        Assert.True(rise > fall);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void ConvergesTowardTarget(double target)
    {
        var smoother = new AudioLevelSmoother();
        smoother.Next(target > 0.5 ? 0 : 1);

        var previousDistance = Math.Abs(target - smoother.Value);
        for (var i = 0; i < 40; i++)
        {
            var distance = Math.Abs(target - smoother.Next(target));
            Assert.True(distance <= previousDistance);
            previousDistance = distance;
        }

        Assert.Equal(target, smoother.Value, precision: 3);
    }

    [Fact]
    public void ClampsInputs()
    {
        var high = new AudioLevelSmoother();
        var one = new AudioLevelSmoother();
        Assert.Equal(one.Next(1), high.Next(5));

        var low = new AudioLevelSmoother();
        Assert.Equal(0, low.Next(-3));

        var nan = new AudioLevelSmoother();
        Assert.Equal(0, nan.Next(double.NaN));

        for (var i = 0; i < 50; i++)
        {
            Assert.InRange(high.Next(i % 2 == 0 ? 100 : -100), 0, 1);
        }
    }

    [Fact]
    public void Reset_ReturnsToZero()
    {
        var smoother = new AudioLevelSmoother();
        smoother.Next(1);
        smoother.Next(1);

        smoother.Reset();

        Assert.Equal(0, smoother.Value);
        Assert.Equal(0.6, smoother.Next(1), precision: 10);
    }
}
