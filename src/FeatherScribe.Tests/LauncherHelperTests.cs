using System.Text.Json.Nodes;
using FeatherScribe.Core;
using FeatherScribe.Launcher;

namespace FeatherScribe.Tests;

// Pure helpers of the console launcher (Phase LAUNCH-2). No Ollama, no processes.
public sealed class LauncherHelperTests
{
    private static readonly OllamaModel[] Models =
    [
        new("gemma3:4b", 3_338_801_804),
        new("gemma4:e2b", 7_162_394_016),
        new("gemma4:e4b", 9_608_350_718),
    ];

    // --- OllamaTags.Parse ---

    [Fact]
    public void Tags_ParsesNamesAndSizesSortedByName()
    {
        const string json = """
            {"models":[
              {"name":"gemma4:e4b","model":"gemma4:e4b","size":9608350718,"digest":"abc"},
              {"name":"hf.co/org/Model-1.5B-GGUF:Q5_K_M","size":1285496960},
              {"name":"gemma4:e2b","size":7162394016,"details":{"family":"gemma4"}}
            ]}
            """;

        var models = OllamaTags.Parse(json);

        Assert.Equal(
            [
                new OllamaModel("gemma4:e2b", 7_162_394_016),
                new OllamaModel("gemma4:e4b", 9_608_350_718),
                new OllamaModel("hf.co/org/Model-1.5B-GGUF:Q5_K_M", 1_285_496_960),
            ],
            models);
    }

    [Theory]
    [InlineData("""{"models":[]}""")]
    [InlineData("""{}""")]
    [InlineData("")]
    [InlineData(null)]
    public void Tags_EmptyListOrNoModels_IsEmpty(string? json)
    {
        Assert.Empty(OllamaTags.Parse(json));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"models":""")]
    [InlineData("""{"models":"gemma4:e2b"}""")]
    [InlineData("""[1,2,3]""")]
    public void Tags_Malformed_IsEmpty(string json)
    {
        Assert.Empty(OllamaTags.Parse(json));
    }

    [Fact]
    public void Tags_SkipsEntriesWithoutNameAndDefaultsMissingSize()
    {
        const string json = """{"models":[{"size":1},{"name":""},{"name":42},"x",{"name":"a:1"},{"name":"b:2","size":"big"}]}""";

        Assert.Equal([new OllamaModel("a:1", 0), new OllamaModel("b:2", 0)], OllamaTags.Parse(json));
    }

    // --- LaunchPrompt.ParseYesNo ---

    [Theory]
    [InlineData("", false, false)]
    [InlineData("", true, true)]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData("  ", true, true)]
    [InlineData("y", false, true)]
    [InlineData("Y", false, true)]
    [InlineData("yes", false, true)]
    [InlineData(" Yes ", false, true)]
    [InlineData("ｙ", false, true)]
    [InlineData("はい", false, true)]
    [InlineData("n", true, false)]
    [InlineData("N", true, false)]
    [InlineData("no", true, false)]
    [InlineData("ｎ", true, false)]
    [InlineData("いいえ", true, false)]
    public void YesNo_ParsesAnswersAndEnterDefault(string? input, bool defaultValue, bool expected)
    {
        Assert.Equal(expected, LaunchPrompt.ParseYesNo(input, defaultValue));
    }

    [Theory]
    [InlineData("x", false)]
    [InlineData("x", true)]
    [InlineData("1", true)]
    [InlineData("yn", false)]
    [InlineData("nope", true)]
    public void YesNo_InvalidInput_IsNull(string input, bool defaultValue)
    {
        Assert.Null(LaunchPrompt.ParseYesNo(input, defaultValue));
    }

    [Fact]
    public void YesNoHint_CapitalizesTheDefault()
    {
        Assert.Equal("[Y/n]", LaunchPrompt.YesNoHint(true));
        Assert.Equal("[y/N]", LaunchPrompt.YesNoHint(false));
    }

    // --- LaunchPrompt.ParseModelChoice ---

    [Theory]
    [InlineData("1", 0)]
    [InlineData("2", 1)]
    [InlineData(" 3 ", 2)]
    [InlineData("３", 2)]
    public void ModelChoice_Number_SelectsThatModel(string input, int expected)
    {
        Assert.Equal(expected, LaunchPrompt.ParseModelChoice(input, Models, "gemma4:e2b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void ModelChoice_Enter_SelectsConfiguredModel(string? input)
    {
        Assert.Equal(1, LaunchPrompt.ParseModelChoice(input, Models, "gemma4:e2b"));
        Assert.Equal(2, LaunchPrompt.ParseModelChoice(input, Models, "GEMMA4:E4B"));
    }

    [Theory]
    [InlineData("hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M")]
    [InlineData("")]
    [InlineData(null)]
    public void ModelChoice_Enter_SelectsFirstWhenConfiguredModelIsMissing(string? configuredModel)
    {
        Assert.Equal(0, LaunchPrompt.ParseModelChoice("", Models, configuredModel));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("99999999999")]
    [InlineData("abc")]
    [InlineData("gemma4:e2b")]
    [InlineData("1 2")]
    public void ModelChoice_OutOfRangeOrGarbage_IsNull(string input)
    {
        Assert.Null(LaunchPrompt.ParseModelChoice(input, Models, "gemma4:e2b"));
    }

    [Fact]
    public void ModelChoice_NoModels_IsNull()
    {
        Assert.Null(LaunchPrompt.ParseModelChoice("", [], "gemma4:e2b"));
        Assert.Null(LaunchPrompt.ParseModelChoice("1", [], "gemma4:e2b"));
    }

    [Fact]
    public void FindModelIndex_MatchesLatestTagWhenNoTagGiven()
    {
        OllamaModel[] models = [new("llama3:latest", 1), new("gemma4:e2b", 2)];

        Assert.Equal(0, LaunchPrompt.FindModelIndex(models, "llama3"));
        Assert.Equal(1, LaunchPrompt.FindModelIndex(models, " gemma4:e2b "));
        Assert.Null(LaunchPrompt.FindModelIndex(models, "gemma4"));
        Assert.Null(LaunchPrompt.FindModelIndex(models, "gemma4:e4b"));
    }

    [Theory]
    [InlineData(7_162_394_016, "7.2 GB")]
    [InlineData(9_608_350_718, "9.6 GB")]
    [InlineData(0, "0.0 GB")]
    [InlineData(-5, "0.0 GB")]
    public void FormatSize_UsesDecimalGigabytes(long bytes, string expected)
    {
        Assert.Equal(expected, LaunchPrompt.FormatSize(bytes));
    }

    // --- AppLaunchArguments ---

    [Fact]
    public void AppArguments_LlmOff_HasNoModel()
    {
        Assert.Equal(["--llm", "off"], AppLaunchArguments.Build(llmEnabled: false, model: "gemma4:e2b", ownedOllamaPid: null));
    }

    [Theory]
    [InlineData("gemma4:e2b")]
    [InlineData("hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M")]
    [InlineData("library/qwen2.5:0.5b")]
    public void AppArguments_LlmOn_PassesModelTagAsOneArgument(string model)
    {
        Assert.Equal(
            ["--llm", "on", "--llm-model", model],
            AppLaunchArguments.Build(llmEnabled: true, model, ownedOllamaPid: null));
    }

    [Fact]
    public void AppArguments_OwnedOllamaPid_IsAppendedWhenPositive()
    {
        Assert.Equal(
            ["--llm", "on", "--llm-model", "gemma4:e2b", "--owned-ollama-pid", "4242"],
            AppLaunchArguments.Build(llmEnabled: true, "gemma4:e2b", 4242));
        Assert.Equal(["--llm", "on"], AppLaunchArguments.Build(llmEnabled: true, model: null, ownedOllamaPid: 0));
        Assert.Equal(["--llm", "on"], AppLaunchArguments.Build(llmEnabled: true, model: " ", ownedOllamaPid: -1));
    }

    [Fact]
    public void FormatCommandLine_LeavesTagsWithColonSlashAndDotUnquoted()
    {
        var args = AppLaunchArguments.Build(llmEnabled: true, "hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M", 12);

        Assert.Equal(
            @"src\FeatherScribe.App\bin\Release\net10.0-windows\FeatherScribe.App.exe --llm on --llm-model hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M --owned-ollama-pid 12",
            AppLaunchArguments.FormatCommandLine(@"src\FeatherScribe.App\bin\Release\net10.0-windows\FeatherScribe.App.exe", args));
    }

    [Theory]
    [InlineData("gemma4:e2b", "gemma4:e2b")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData(@"My Apps\x.exe", "\"My Apps\\x.exe\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"dir with space\", "\"dir with space\\\\\"")]
    public void Quote_FollowsWindowsCommandLineRules(string argument, string expected)
    {
        Assert.Equal(expected, AppLaunchArguments.Quote(argument));
    }

    // --- Keep-alive default ---

    [Theory]
    [InlineData(null, "60m")]
    [InlineData("", "60m")]
    [InlineData("  ", "60m")]
    [InlineData("30m", "30m")]
    [InlineData(" -1 ", "-1")]
    public void EffectiveKeepAlive_ConfiguredOrSixtyMinutes(string? configured, string expected)
    {
        Assert.Equal(expected, LaunchDefaults.EffectiveKeepAlive(configured));
        Assert.Equal("60m", LlmSettings.LauncherDefaultKeepAlive);
    }

    // --- Ollama load request ---

    [Fact]
    public void LoadRequest_HasModelAndKeepAliveButNoPrompt()
    {
        var json = JsonNode.Parse(OllamaApi.BuildLoadRequestJson("gemma4:e2b", "60m", gpuLayers: null, numContext: null))!.AsObject();

        Assert.Equal("gemma4:e2b", (string?)json["model"]);
        Assert.Equal("60m", (string?)json["keep_alive"]);
        Assert.False(json.ContainsKey("prompt"));
        Assert.False(json.ContainsKey("options"));
    }

    [Fact]
    public void LoadRequest_SendsGpuLayersAndContextLikeTheFormatter()
    {
        var json = JsonNode.Parse(OllamaApi.BuildLoadRequestJson("gemma4:e2b", "30m", gpuLayers: 0, numContext: 1024))!.AsObject();

        Assert.Equal(0, (int?)json["options"]?["num_gpu"]);
        Assert.Equal(1024, (int?)json["options"]?["num_ctx"]);
    }

    [Theory]
    [InlineData("""{"error":"model 'x' not found"}""", "model 'x' not found")]
    [InlineData("""{"done":true,"done_reason":"load"}""", null)]
    [InlineData("oops", null)]
    [InlineData("", null)]
    public void ParseError_ReadsOllamaErrorText(string json, string? expected)
    {
        Assert.Equal(expected, OllamaApi.ParseError(json));
    }

    // --- Launcher command line ---

    [Fact]
    public void LauncherArguments_ParsesAppLlmAndModel()
    {
        var parsed = LauncherArguments.Parse(
            ["--app", @"src\FeatherScribe.App\bin\Release\net10.0-windows\FeatherScribe.App.exe", "--llm", "ON", "--model", "gemma4:e2b"]);

        Assert.Equal(@"src\FeatherScribe.App\bin\Release\net10.0-windows\FeatherScribe.App.exe", parsed.AppPath);
        Assert.True(parsed.Llm);
        Assert.Equal("gemma4:e2b", parsed.Model);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void LauncherArguments_InvalidOrMissingValuesAreIgnoredWithWarnings()
    {
        var parsed = LauncherArguments.Parse(["--llm", "maybe", "--model", "--llm", "off", "--extra"]);

        Assert.Null(parsed.AppPath);
        Assert.False(parsed.Llm); // the later valid --llm off wins
        Assert.Null(parsed.Model);
        Assert.Equal(3, parsed.Warnings.Count);
    }
}
