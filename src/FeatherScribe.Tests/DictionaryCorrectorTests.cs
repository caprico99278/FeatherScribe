using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public class DictionaryCorrectorTests
{
    private static DictionaryCorrector CreateCorrector() => new(
    [
        new DictionaryEntry(["うぃすぱー", "ウィスパー"], "whisper.cpp"),
        new DictionaryEntry(["じぇま", "ジェマ"], "Gemma 4"),
        new DictionaryEntry(["ふぇにっくすくおんつ", "フェニックスクオンツ"], "PhoenixQuant"),
    ]);

    [Fact]
    public void Correct_ReplacesSinglePattern()
    {
        var corrector = CreateCorrector();

        var result = corrector.Correct("ウィスパーで文字起こしする");

        Assert.Equal("whisper.cppで文字起こしする", result);
    }

    [Fact]
    public void Correct_ReplacesMultiplePatternsInOneText()
    {
        var corrector = CreateCorrector();

        var result = corrector.Correct("うぃすぱーの結果をじぇまで整形");

        Assert.Equal("whisper.cppの結果をGemma 4で整形", result);
    }

    [Fact]
    public void Correct_PrefersLongerPatternFirst()
    {
        var corrector = new DictionaryCorrector(
        [
            new DictionaryEntry(["じぇま"], "Gemma 4"),
            new DictionaryEntry(["じぇまふぉー"], "Gemma 4 (long)"),
        ]);

        var result = corrector.Correct("じぇまふぉーを使う");

        Assert.Equal("Gemma 4 (long)を使う", result);
    }

    [Fact]
    public void Correct_EmptyText_ReturnsAsIs()
    {
        var corrector = CreateCorrector();

        Assert.Equal("", corrector.Correct(""));
    }

    [Fact]
    public void Correct_NoMatch_ReturnsOriginal()
    {
        var corrector = CreateCorrector();

        Assert.Equal("こんにちは", corrector.Correct("こんにちは"));
    }

    [Fact]
    public void Correct_EmptyDictionary_ReturnsOriginal()
    {
        var corrector = new DictionaryCorrector([]);

        Assert.Equal("ウィスパー", corrector.Correct("ウィスパー"));
    }
}
