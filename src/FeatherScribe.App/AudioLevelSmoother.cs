namespace FeatherScribe.App;

/// <summary>
/// Exponential moving average for the recording level meter: rises quickly while speaking,
/// settles more slowly while silent. Pure; one step per received level (no timer).
/// </summary>
internal sealed class AudioLevelSmoother
{
    public const double Attack = 0.6;
    public const double Release = 0.25;

    public double Value { get; private set; }

    /// <summary>Advances one step toward <paramref name="target"/> (clamped to 0..1) and returns the new value.</summary>
    public double Next(double target)
    {
        if (double.IsNaN(target))
        {
            target = 0;
        }

        target = Math.Clamp(target, 0, 1);
        var factor = target > Value ? Attack : Release;
        Value += (target - Value) * factor;
        return Value;
    }

    public void Reset()
    {
        Value = 0;
    }
}
