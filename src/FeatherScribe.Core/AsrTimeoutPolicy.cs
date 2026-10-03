namespace FeatherScribe.Core;

/// <summary>
/// Effective whisper timeout for one recording. A fixed timeout cancels long recordings on CPU-only
/// machines (whisper needs about 0.5–1.1× the audio duration), and the audio is deleted afterwards, so
/// the spoken content would be lost. The configured <c>asr.timeoutSeconds</c> is the minimum; the
/// timeout grows with the audio: <c>max(configured, ceil(audio seconds × 3) + 30)</c>.
/// </summary>
public static class AsrTimeoutPolicy
{
    public const int AudioDurationMultiplier = 3;
    public const int AudioDurationMarginSeconds = 30;

    /// <summary>Upper bound (24 h) so the value always fits a cancellation timer.</summary>
    public const int MaxTimeoutSeconds = 86_400;

    /// <summary>Timeout in seconds; zero or negative audio duration (unknown) keeps the configured value.</summary>
    public static int For(int configuredSeconds, TimeSpan audioDuration)
    {
        var configured = Math.Min(configuredSeconds, MaxTimeoutSeconds);
        if (audioDuration <= TimeSpan.Zero)
        {
            return configured;
        }

        var computed = Math.Ceiling(audioDuration.TotalSeconds * AudioDurationMultiplier) + AudioDurationMarginSeconds;
        var bounded = (int)Math.Min(computed, MaxTimeoutSeconds);
        return Math.Max(configured, bounded);
    }
}
