using System.Text;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

public class JsonAppSettingsProviderTests : IDisposable
{
    private readonly string _rootPath;

    public JsonAppSettingsProviderTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "FeatherScribeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "config"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootPath, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void WriteSettings(string json)
        => File.WriteAllText(Path.Combine(_rootPath, "config", "appsettings.json"), json, Encoding.UTF8);

    private void WriteLocalSettings(string json)
        => File.WriteAllText(Path.Combine(_rootPath, "config", "appsettings.local.json"), json, Encoding.UTF8);

    [Fact]
    public void Load_ValidSettings_ParsesValues()
    {
        WriteSettings("""
        {
          "asr": { "whisperExecutablePath": "C:/tools/whisper-cli.exe", "modelPath": "C:/models/ggml-small.bin", "language": "ja", "threads": 8, "timeoutSeconds": 60 },
          "llm": { "provider": "ollama", "endpoint": "http://localhost:11434", "model": "gemma4:e4b", "temperature": 0.2, "timeoutSeconds": 90 },
          "output": { "mode": "ClipboardOnly", "pasteDelayMilliseconds": 200 },
          "privacy": { "saveAudioFiles": false, "saveRawTranscript": false, "saveFormattedText": false }
        }
        """);

        var provider = new JsonAppSettingsProvider(_rootPath);
        var settings = provider.Load();

        Assert.Equal("C:/tools/whisper-cli.exe", settings.Asr.WhisperExecutablePath);
        Assert.Equal(8, settings.Asr.Threads);
        Assert.Equal("gemma4:e4b", settings.Llm.Model);
        Assert.Equal(0.2, settings.Llm.Temperature);
        Assert.Equal(OutputMode.ClipboardOnly, settings.Output.ParsedMode);
        Assert.Equal(200, settings.Output.PasteDelayMilliseconds);
        Assert.Equal([Path.Combine(_rootPath, "config", "appsettings.json")], provider.LoadedSettingsPaths);
    }

    [Fact]
    public void Load_LocalSettings_OverridesOnlySpecifiedProperties()
    {
        WriteSettings("""
        {
          "llm": {
            "enabled": false,
            "provider": "ollama",
            "endpoint": "http://localhost:11434",
            "model": "gemma4:e2b",
            "qualityModel": "gemma4:e4b",
            "temperature": 0.1,
            "timeoutSeconds": 8,
            "qualityTimeoutSeconds": 300,
            "fallbackToRaw": true,
            "rawFirstPaste": true
          }
        }
        """);
        WriteLocalSettings("""
        {
          "llm": {
            "enabled": true,
            "model": "hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M",
            "timeoutSeconds": 10
          }
        }
        """);

        var provider = new JsonAppSettingsProvider(_rootPath);
        var settings = provider.Load();

        Assert.True(settings.Llm.Enabled);
        Assert.Equal("hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M", settings.Llm.Model);
        Assert.Equal(10, settings.Llm.TimeoutSeconds);
        Assert.Equal("ollama", settings.Llm.Provider);
        Assert.Equal("http://localhost:11434", settings.Llm.Endpoint);
        Assert.Equal("gemma4:e4b", settings.Llm.QualityModel);
        Assert.True(settings.Llm.FallbackToRaw);
        Assert.True(settings.Llm.RawFirstPaste);
        Assert.Equal(
            [
                Path.Combine(_rootPath, "config", "appsettings.json"),
                Path.Combine(_rootPath, "config", "appsettings.local.json"),
            ],
            provider.LoadedSettingsPaths);
    }

    [Fact]
    public void Load_LocalLlmEnabledOnly_KeepsOtherLlmValues()
    {
        WriteSettings("""
        {
          "llm": {
            "enabled": false,
            "endpoint": "http://localhost:11434",
            "model": "gemma4:e2b",
            "qualityModel": "gemma4:e4b",
            "timeoutSeconds": 8,
            "fallbackToRaw": true,
            "rawFirstPaste": true
          }
        }
        """);
        WriteLocalSettings("""{ "llm": { "enabled": true } }""");

        var settings = new JsonAppSettingsProvider(_rootPath).Load();

        Assert.True(settings.Llm.Enabled);
        Assert.Equal("http://localhost:11434", settings.Llm.Endpoint);
        Assert.Equal("gemma4:e2b", settings.Llm.Model);
        Assert.Equal("gemma4:e4b", settings.Llm.QualityModel);
        Assert.Equal(8, settings.Llm.TimeoutSeconds);
        Assert.True(settings.Llm.FallbackToRaw);
        Assert.True(settings.Llm.RawFirstPaste);
    }

    [Fact]
    public void Load_BrokenLocalJson_UsesBaseSettingsWithWarning()
    {
        WriteSettings("""
        {
          "llm": {
            "enabled": false,
            "model": "gemma4:e2b",
            "timeoutSeconds": 8
          }
        }
        """);
        WriteLocalSettings("{ this is not json ");

        var provider = new JsonAppSettingsProvider(_rootPath);
        var settings = provider.Load();

        Assert.False(settings.Llm.Enabled);
        Assert.Equal("gemma4:e2b", settings.Llm.Model);
        Assert.Equal(8, settings.Llm.TimeoutSeconds);
        Assert.Null(provider.LastError);
        Assert.Contains(provider.LastWarnings, warning => warning.Contains("appsettings.local.json", StringComparison.Ordinal));
        Assert.Equal([Path.Combine(_rootPath, "config", "appsettings.json")], provider.LoadedSettingsPaths);
    }

    [Fact]
    public void Load_RelativeAsrPaths_ResolvedAgainstRoot()
    {
        WriteSettings("""
        { "asr": { "whisperExecutablePath": "local/whisper/whisper-cli.exe", "modelPath": "local/models/ggml-small.bin" } }
        """);

        var settings = new JsonAppSettingsProvider(_rootPath).Load();

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_rootPath, "local", "whisper", "whisper-cli.exe")),
            settings.Asr.WhisperExecutablePath);
        Assert.True(Path.IsPathRooted(settings.Asr.ModelPath));
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsWithError()
    {
        var provider = new JsonAppSettingsProvider(
            Path.Combine(_rootPath, "does-not-exist"));

        var settings = provider.Load();

        Assert.NotNull(provider.LastError);
        Assert.Equal("ja", settings.Asr.Language);
        // 初期UX方針: LLM整形は既定OFF、軽量モデル優先、短タイムアウト、raw fallback
        Assert.False(settings.Llm.Enabled);
        Assert.Equal("gemma4:e2b", settings.Llm.Model);
        Assert.Equal("gemma4:e4b", settings.Llm.QualityModel);
        Assert.Equal(8, settings.Llm.TimeoutSeconds);
        Assert.True(settings.Llm.FallbackToRaw);
        Assert.Equal(OutputMode.ClipboardAndPaste, settings.Output.ParsedMode);
    }

    [Fact]
    public void Load_BrokenJson_ReturnsDefaultsWithError()
    {
        WriteSettings("{ this is not json ");

        var provider = new JsonAppSettingsProvider(_rootPath);
        var settings = provider.Load();

        Assert.NotNull(provider.LastError);
        Assert.Equal("ollama", settings.Llm.Provider);
    }

    [Fact]
    public void Load_InvalidOutputMode_FallsBackToClipboardAndPaste()
    {
        WriteSettings("""{ "output": { "mode": "TeleportText" } }""");

        var settings = new JsonAppSettingsProvider(_rootPath).Load();

        Assert.Equal(OutputMode.ClipboardAndPaste, settings.Output.ParsedMode);
    }

    [Fact]
    public void Load_ZeroThreads_ClampedToOne()
    {
        WriteSettings("""{ "asr": { "threads": 0 } }""");

        var settings = new JsonAppSettingsProvider(_rootPath).Load();

        Assert.Equal(1, settings.Asr.Threads);
    }
}
