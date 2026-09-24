using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public class PromptBuilderTests
{
    private const string Template =
        "整形してください。\n用語辞書:\n{{dictionary}}\n文字起こし:\n<<<\n{{raw_transcript}}\n>>>";

    [Fact]
    public void Build_ReplacesTranscriptPlaceholder()
    {
        var prompt = PromptBuilder.Build(Template, [], "えーとテストです");

        Assert.Contains("えーとテストです", prompt);
        Assert.DoesNotContain("{{raw_transcript}}", prompt);
    }

    [Fact]
    public void Build_ReplacesDictionaryPlaceholderWithEntries()
    {
        var entries = new List<DictionaryEntry>
        {
            new(["うぃすぱー", "ウィスパー"], "whisper.cpp"),
        };

        var prompt = PromptBuilder.Build(Template, entries, "テスト");

        Assert.Contains("- うぃすぱー, ウィスパー => whisper.cpp", prompt);
        Assert.DoesNotContain("{{dictionary}}", prompt);
    }

    [Fact]
    public void Build_EmptyDictionary_UsesPlaceholderText()
    {
        var prompt = PromptBuilder.Build(Template, [], "テスト");

        Assert.Contains("(辞書なし)", prompt);
    }

    [Fact]
    public void BuildDictionarySection_MultipleEntries_OnePerLine()
    {
        var section = PromptBuilder.BuildDictionarySection(
        [
            new DictionaryEntry(["あ"], "A"),
            new DictionaryEntry(["い"], "B"),
        ]);

        var lines = section.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("- あ => A", lines[0]);
        Assert.Equal("- い => B", lines[1]);
    }

    [Fact]
    public void Build_NullTemplate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PromptBuilder.Build(null!, [], "x"));
    }
}
