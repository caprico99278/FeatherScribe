using FeatherScribe.Core;

namespace FeatherScribe.Tests;

/// <summary>
/// The whisper timeout grows with the recording (configured value = minimum), so a long CPU-only
/// transcription is not cancelled and the deleted audio's content is not lost.
/// </summary>
public sealed class AsrTimeoutPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void UnknownOrNegativeDuration_KeepsConfiguredValue(int audioSeconds)
    {
        Assert.Equal(120, AsrTimeoutPolicy.For(120, TimeSpan.FromSeconds(audioSeconds)));
    }

    [Fact]
    public void MaxRecording_GetsThreeTimesTheAudioPlusMargin()
    {
        // 300 s (the default recording limit) → 300 × 3 + 30.
        Assert.Equal(930, AsrTimeoutPolicy.For(120, TimeSpan.FromSeconds(300)));
    }

    [Theory]
    [InlineData(10, 120)]  // 10 × 3 + 30 = 60 < 120
    [InlineData(30, 120)]  // 30 × 3 + 30 = 120 = configured
    [InlineData(31, 123)]  // 31 × 3 + 30 = 123
    public void ShortRecording_UsesConfiguredValueAsMinimum(int audioSeconds, int expected)
    {
        Assert.Equal(expected, AsrTimeoutPolicy.For(120, TimeSpan.FromSeconds(audioSeconds)));
    }

    [Fact]
    public void ConfiguredLargerThanComputed_KeepsConfiguredValue()
    {
        Assert.Equal(2000, AsrTimeoutPolicy.For(2000, TimeSpan.FromSeconds(300)));
    }

    [Fact]
    public void FractionalSeconds_RoundUp()
    {
        // 100.1 × 3 = 300.3 → 301, + 30.
        Assert.Equal(331, AsrTimeoutPolicy.For(1, TimeSpan.FromMilliseconds(100_100)));
    }

    [Fact]
    public void HugeValues_AreBoundedForTheCancellationTimer()
    {
        Assert.Equal(AsrTimeoutPolicy.MaxTimeoutSeconds, AsrTimeoutPolicy.For(120, TimeSpan.FromDays(30)));
        Assert.Equal(AsrTimeoutPolicy.MaxTimeoutSeconds, AsrTimeoutPolicy.For(int.MaxValue, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void WhisperEngine_UsesPolicyWithAudioDurationAndReportsEffectiveSeconds()
    {
        var code = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "FeatherScribe.Infrastructure", "WhisperCppTranscriptionEngine.cs"));

        Assert.Contains("AsrTimeoutPolicy.For(_settings.TimeoutSeconds, audioFile.Duration)", code);
        Assert.Contains("timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));", code);
        Assert.Contains("whisper-cli がタイムアウトしました ({timeoutSeconds}秒)", code);
        Assert.DoesNotContain("TimeSpan.FromSeconds(_settings.TimeoutSeconds)", code);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FeatherScribe.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("FeatherScribe.slnx was not found.");
    }
}
