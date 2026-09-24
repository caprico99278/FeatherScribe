using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

public class WhisperCppCommandBuilderTests
{
    private static readonly AsrSettings Settings = new()
    {
        WhisperExecutablePath = @"C:\tools\whisper-cli.exe",
        ModelPath = @"C:\models\ggml-small.bin",
        Language = "ja",
        Threads = 4,
    };

    [Fact]
    public void BuildArguments_ContainsModelLanguageAndInput()
    {
        var args = WhisperCppCommandBuilder.BuildArguments(
            Settings, @"C:\temp\audio.wav", @"C:\temp\audio_asr");

        Assert.Equal(
            ["-m", @"C:\models\ggml-small.bin", "-l", "ja", "-t", "4",
             "--output-txt", "--output-file", @"C:\temp\audio_asr", "--no-prints",
             "-f", @"C:\temp\audio.wav"],
            args);
    }

    [Fact]
    public void BuildArguments_EmptyLanguage_DefaultsToJapanese()
    {
        var settings = new AsrSettings { Language = "" };

        var args = WhisperCppCommandBuilder.BuildArguments(settings, "a.wav", "out");

        var languageIndex = args.ToList().IndexOf("-l");
        Assert.Equal("ja", args[languageIndex + 1]);
    }

    [Fact]
    public void BuildArguments_ZeroThreads_ClampedToOne()
    {
        var settings = new AsrSettings { Threads = 0 };

        var args = WhisperCppCommandBuilder.BuildArguments(settings, "a.wav", "out");

        var threadsIndex = args.ToList().IndexOf("-t");
        Assert.Equal("1", args[threadsIndex + 1]);
    }

    [Fact]
    public void BuildArguments_EmptyAudioPath_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => WhisperCppCommandBuilder.BuildArguments(Settings, "", "out"));
    }
}
