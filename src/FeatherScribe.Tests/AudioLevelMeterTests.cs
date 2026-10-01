using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

public sealed class AudioLevelMeterTests
{
    [Fact]
    public void Zeros_ReturnZero()
    {
        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(new byte[3200]));
        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(new byte[3200], 3200));
    }

    [Fact]
    public void Empty_ReturnsZero()
    {
        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(ReadOnlySpan<byte>.Empty));
        Assert.Equal(0f, AudioLevelMeter.ComputeLevel([], 0));
        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(new byte[] { 0x7F }));
        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(new byte[64], 0));
    }

    [Fact]
    public void TinyAmplitude_IsBelowLargeAmplitude_AndBothWithinRange()
    {
        var tiny = AudioLevelMeter.ComputeLevel(SquareWave(30, 800));
        var quiet = AudioLevelMeter.ComputeLevel(SquareWave(300, 800));
        var large = AudioLevelMeter.ComputeLevel(SquareWave(8000, 800));

        Assert.InRange(tiny, 0f, 1f);
        Assert.InRange(quiet, 0f, 1f);
        Assert.InRange(large, 0f, 1f);
        Assert.True(tiny < large, $"tiny {tiny} should be below large {large}");
        // ±300 (about -41 dBFS) is between the floor and the ceiling, so it maps strictly inside 0..1.
        Assert.True(quiet > 0f && quiet < large, $"quiet {quiet} should be in (0, {large})");
    }

    [Fact]
    public void FullScaleSquareWave_ReturnsOne()
    {
        var buffer = new byte[1600];
        for (var i = 0; i < buffer.Length / 2; i++)
        {
            var sample = i % 2 == 0 ? short.MaxValue : short.MinValue;
            buffer[i * 2] = (byte)(sample & 0xFF);
            buffer[(i * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }

        Assert.Equal(1f, AudioLevelMeter.ComputeLevel(buffer));
    }

    [Fact]
    public void MatchesDecibelMapping()
    {
        // rms 300 → 20*log10(300/32768) ≈ -40.77 dBFS → (-40.77 + 50) / 40 ≈ 0.2308
        var expected = (20 * Math.Log10(300 / 32768.0) - AudioLevelMeter.FloorDb)
            / (AudioLevelMeter.CeilingDb - AudioLevelMeter.FloorDb);

        Assert.Equal(expected, AudioLevelMeter.ComputeLevel(SquareWave(300, 800)), precision: 4);
        Assert.Equal(-50, AudioLevelMeter.FloorDb);
        Assert.Equal(-10, AudioLevelMeter.CeilingDb);
    }

    [Fact]
    public void OddTrailingByte_IsIgnored()
    {
        var even = SquareWave(2000, 100);
        var odd = new byte[even.Length + 1];
        even.CopyTo(odd, 0);
        odd[^1] = 0x7F;

        Assert.Equal(AudioLevelMeter.ComputeLevel(even), AudioLevelMeter.ComputeLevel(odd));
        Assert.Equal(AudioLevelMeter.ComputeLevel(even), AudioLevelMeter.ComputeLevel(odd, odd.Length));
    }

    [Fact]
    public void BytesRecorded_LimitsTheMeasuredRegion()
    {
        var buffer = new byte[3200];
        SquareWave(8000, 800).CopyTo(buffer, 0);

        // Only the first 1600 bytes carry the signal; the rest of the buffer is silence.
        Assert.Equal(AudioLevelMeter.ComputeLevel(buffer.AsSpan(0, 1600)), AudioLevelMeter.ComputeLevel(buffer, 1600));
        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(buffer.AsSpan(1600)));
        // Out-of-range counts are clamped instead of throwing on the audio thread.
        Assert.InRange(AudioLevelMeter.ComputeLevel(buffer, 100000), 0f, 1f);
        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(buffer, -5));
    }

    [Fact]
    public void RandomBuffers_AlwaysWithinZeroAndOne()
    {
        var random = new Random(1234);
        for (var i = 0; i < 200; i++)
        {
            var buffer = new byte[random.Next(0, 4000)];
            random.NextBytes(buffer);

            Assert.InRange(AudioLevelMeter.ComputeLevel(buffer), 0f, 1f);
        }
    }

    private static byte[] SquareWave(short amplitude, int sampleCount)
    {
        var buffer = new byte[sampleCount * 2];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(i % 2 == 0 ? amplitude : -amplitude);
            buffer[i * 2] = (byte)(sample & 0xFF);
            buffer[(i * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return buffer;
    }
}
