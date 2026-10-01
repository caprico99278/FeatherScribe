using System.Buffers.Binary;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// Converts one 16-bit PCM buffer into a perceptual 0..1 level for the recording meter.
/// Pure: reads the span in place, copies no samples, and stores nothing.
/// </summary>
public static class AudioLevelMeter
{
    /// <summary>Levels at or below this dBFS map to 0.</summary>
    public const double FloorDb = -50;

    /// <summary>Levels at or above this dBFS map to 1.</summary>
    public const double CeilingDb = -10;

    private const double FullScale = 32768;

    public static float ComputeLevel(byte[] buffer, int bytesRecorded)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var length = Math.Clamp(bytesRecorded, 0, buffer.Length);
        return ComputeLevel(new ReadOnlySpan<byte>(buffer, 0, length));
    }

    /// <summary>
    /// Little-endian 16-bit signed samples; a trailing odd byte is ignored. Empty or silent input returns 0.
    /// </summary>
    public static float ComputeLevel(ReadOnlySpan<byte> pcm16)
    {
        var sampleCount = pcm16.Length / 2;
        if (sampleCount == 0)
        {
            return 0f;
        }

        double sumOfSquares = 0;
        for (var i = 0; i < sampleCount; i++)
        {
            double sample = BinaryPrimitives.ReadInt16LittleEndian(pcm16.Slice(i * 2, 2));
            sumOfSquares += sample * sample;
        }

        var rms = Math.Sqrt(sumOfSquares / sampleCount);
        if (rms <= 0)
        {
            return 0f;
        }

        var db = 20 * Math.Log10(rms / FullScale);
        var level = (db - FloorDb) / (CeilingDb - FloorDb);
        return (float)Math.Clamp(level, 0, 1);
    }
}
