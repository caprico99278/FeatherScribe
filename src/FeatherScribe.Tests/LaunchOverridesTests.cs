using FeatherScribe.App;
using FeatherScribe.Core;

namespace FeatherScribe.Tests;

// App command-line overrides from the launcher (Phase LAUNCH-2): parsing, in-memory application and the
// owned-Ollama safety predicate. No processes are started or stopped here.
public sealed class LaunchOverridesTests
{
    // --- Parse ---

    [Fact]
    public void Parse_ValidArguments()
    {
        var overrides = LaunchOverrides.Parse(
            ["--llm", "on", "--llm-model", "hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M", "--owned-ollama-pid", "4242"]);

        Assert.Equal(new LaunchOverrides(true, "hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M", 4242), overrides);
    }

    [Theory]
    [InlineData("off", false)]
    [InlineData("OFF", false)]
    [InlineData("On", true)]
    public void Parse_LlmOnOffIsCaseInsensitive(string value, bool expected)
    {
        Assert.Equal(expected, LaunchOverrides.Parse(["--llm", value]).LlmEnabled);
    }

    [Fact]
    public void Parse_NoArguments_IsNone()
    {
        Assert.Equal(LaunchOverrides.None, LaunchOverrides.Parse([]));
    }

    [Theory]
    [InlineData("--llm")]
    [InlineData("--llm-model")]
    [InlineData("--owned-ollama-pid")]
    public void Parse_MissingValue_IsIgnored(string option)
    {
        Assert.Equal(LaunchOverrides.None, LaunchOverrides.Parse([option]));
        Assert.Equal(LaunchOverrides.None, LaunchOverrides.Parse([option, " "]));
    }

    [Fact]
    public void Parse_MissingValueDoesNotSwallowTheNextSwitch()
    {
        var overrides = LaunchOverrides.Parse(["--llm-model", "--llm", "on"]);

        Assert.Equal(new LaunchOverrides(true, null, null), overrides);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("1")]
    [InlineData("true")]
    public void Parse_InvalidLlmValue_IsIgnored(string value)
    {
        Assert.Null(LaunchOverrides.Parse(["--llm", value]).LlmEnabled);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("12x")]
    [InlineData("99999999999")]
    [InlineData("+7")]
    public void Parse_BadPid_IsIgnored(string value)
    {
        Assert.Null(LaunchOverrides.Parse(["--owned-ollama-pid", value]).OwnedOllamaPid);
    }

    [Fact]
    public void Parse_UnknownArguments_AreIgnored()
    {
        var overrides = LaunchOverrides.Parse(["--verbose", "file.txt", "--llm", "off", "/x"]);

        Assert.Equal(new LaunchOverrides(false, null, null), overrides);
    }

    // --- Apply ---

    [Fact]
    public void Apply_None_ReturnsTheSameSettings()
    {
        var settings = new AppSettings();

        Assert.Same(settings, LaunchOverrides.None.Apply(settings));
        Assert.Same(settings, new LaunchOverrides(null, null, 1234).Apply(settings));
    }

    [Fact]
    public void Apply_LlmOn_ReplacesEnabledAndModelAndDefaultsKeepAlive()
    {
        var settings = new AppSettings { Llm = new LlmSettings { Enabled = false, Model = "gemma4:e2b", GpuLayers = 0 } };

        var applied = new LaunchOverrides(true, "gemma4:e4b", 10).Apply(settings);

        Assert.True(applied.Llm.Enabled);
        Assert.Equal("gemma4:e4b", applied.Llm.Model);
        Assert.Equal(LlmSettings.LauncherDefaultKeepAlive, applied.Llm.KeepAlive);
        Assert.Equal(0, applied.Llm.GpuLayers);
        Assert.Same(settings.Hotkeys, applied.Hotkeys);
        Assert.False(settings.Llm.Enabled); // the loaded settings object is not changed
    }

    [Fact]
    public void Apply_LlmOn_KeepsConfiguredKeepAlive()
    {
        var settings = new AppSettings { Llm = new LlmSettings { KeepAlive = "30m" } };

        Assert.Equal("30m", new LaunchOverrides(true, null, null).Apply(settings).Llm.KeepAlive);
    }

    [Fact]
    public void Apply_LlmOff_DoesNotDefaultKeepAlive()
    {
        var settings = new AppSettings { Llm = new LlmSettings { Enabled = true } };

        var applied = new LaunchOverrides(false, null, null).Apply(settings);

        Assert.False(applied.Llm.Enabled);
        Assert.Null(applied.Llm.KeepAlive);
        Assert.Equal(settings.Llm.Model, applied.Llm.Model);
    }

    [Fact]
    public void SettingsCopy_CopiesEveryLlmPropertyExceptTheOverriddenOnes()
    {
        var source = new LlmSettings
        {
            Enabled = false,
            Provider = "p",
            Endpoint = "http://localhost:1",
            Model = "m",
            QualityModel = "q",
            Temperature = 0.7,
            NumPredict = 11,
            NumContext = 2048,
            KeepAlive = "5m",
            TimeoutSeconds = 3,
            QualityTimeoutSeconds = 4,
            FallbackToRaw = false,
            RawFirstPaste = false,
            GpuLayers = 7,
        };

        var copy = SettingsCopy.WithLlmOverrides(source, true, "other", "9m");

        var overridden = new Dictionary<string, object?>
        {
            [nameof(LlmSettings.Enabled)] = true,
            [nameof(LlmSettings.Model)] = "other",
            [nameof(LlmSettings.KeepAlive)] = "9m",
        };
        foreach (var property in typeof(LlmSettings).GetProperties())
        {
            var expected = overridden.TryGetValue(property.Name, out var value) ? value : property.GetValue(source);
            Assert.Equal(expected, property.GetValue(copy));
        }
    }

    [Fact]
    public void SettingsCopy_CopiesEveryAppSettingsSection()
    {
        var source = new AppSettings();
        var llm = new LlmSettings();

        var copy = SettingsCopy.WithLlm(source, llm);

        foreach (var property in typeof(AppSettings).GetProperties())
        {
            var expected = property.Name == nameof(AppSettings.Llm) ? llm : property.GetValue(source);
            Assert.Same(expected, property.GetValue(copy));
        }
    }

    // --- Owned Ollama safety predicate ---

    private const string Expected = @"repo\local\ollama\ollama.exe";

    [Theory]
    [InlineData("ollama", @"repo\local\ollama\ollama.exe", true)]
    [InlineData("Ollama", @"REPO\LOCAL\OLLAMA\OLLAMA.EXE", true)]
    [InlineData("ollama", @"other\ollama\ollama.exe", false)]
    [InlineData("ollama", @"repo\local\ollama\ollama app.exe", false)]
    [InlineData("ollama app", @"repo\local\ollama\ollama.exe", false)]
    [InlineData("notepad", @"repo\local\ollama\ollama.exe", false)]
    [InlineData("ollama", null, false)]
    [InlineData("ollama", "", false)]
    [InlineData(null, @"repo\local\ollama\ollama.exe", false)]
    public void IsOwnedOllama_RequiresNameAndExactPath(string? processName, string? path, bool expected)
    {
        Assert.Equal(expected, OwnedOllamaProcess.IsOwnedOllama(processName, path, Expected));
    }

    [Fact]
    public void IsOwnedOllama_EmptyExpectedPath_IsNeverOwned()
    {
        Assert.False(OwnedOllamaProcess.IsOwnedOllama("ollama", "", ""));
    }

    [Fact]
    public void ExpectedExecutablePath_IsUnderLocalOllama()
    {
        var root = Path.Combine(Path.GetTempPath(), "fs-root");

        Assert.Equal(
            Path.Combine(root, "local", "ollama", "ollama.exe"),
            OwnedOllamaProcess.ExpectedExecutablePath(root));
    }
}
