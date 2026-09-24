using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests.Integration;

/// <summary>
/// whisper.cpp CLI 疎通テスト。ローカルに whisper-cli とモデルがある場合のみ実行する。
/// 通常CIでは実行しない: dotnet test --filter "Category!=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class WhisperCliIntegrationTests
{
    private static AppSettings? TryLoadLocalSettings()
    {
        var root = AppRoot.Locate();
        if (!File.Exists(Path.Combine(root, "config", "appsettings.json")))
        {
            return null;
        }

        var settings = new JsonAppSettingsProvider(root).Load();
        return File.Exists(settings.Asr.WhisperExecutablePath) && File.Exists(settings.Asr.ModelPath)
            ? settings
            : null;
    }

    [Fact]
    public async Task Transcribe_SampleWav_ReturnsJapaneseText()
    {
        var settings = TryLoadLocalSettings();
        var sampleWav = Path.Combine(AppRoot.Locate(), "samples", "audio", "sample_001.wav");
        if (settings is null || !File.Exists(sampleWav))
        {
            return; // ローカルスタック未整備のためスキップ
        }

        var engine = new WhisperCppTranscriptionEngine(settings.Asr);
        var result = await engine.TranscribeAsync(
            new AudioFile(sampleWav, TimeSpan.Zero), CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.RawText));
    }

    [Fact]
    public async Task Transcribe_MissingExecutable_FailsWithMessage()
    {
        var engine = new WhisperCppTranscriptionEngine(new AsrSettings
        {
            WhisperExecutablePath = @"C:\does\not\exist\whisper-cli.exe",
            ModelPath = @"C:\does\not\exist\model.bin",
        });

        var result = await engine.TranscribeAsync(
            new AudioFile("dummy.wav", TimeSpan.Zero), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("whisper-cli", result.ErrorMessage);
    }
}
