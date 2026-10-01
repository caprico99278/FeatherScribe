using System.Text;
using FeatherScribe.Tools.AsrBenchmark;

namespace FeatherScribe.Tests;

public class AsrCorpusTests
{
    private static readonly string CorpusPath = Path.Combine(FindRepoRoot(), "benchmark", "asr", "corpus.json");

    private static IReadOnlyList<CorpusItem> Corpus => AsrCorpus.Load(CorpusPath);

    [Fact]
    public void Corpus_Has30UtterancesWithUniqueSequentialIds()
    {
        var corpus = Corpus;

        Assert.Equal(30, corpus.Count);
        Assert.Equal(
            Enumerable.Range(1, 30).Select(i => $"u{i:00}"),
            corpus.Select(item => item.Id));
        Assert.Equal(corpus.Count, corpus.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Corpus_CoversEveryCategoryAtLeastTwiceAndNothingElse()
    {
        var counts = Corpus.GroupBy(item => item.Category).ToDictionary(g => g.Key, g => g.Count());

        foreach (var category in AsrCorpus.RequiredCategories)
        {
            Assert.True(counts.TryGetValue(category, out var count) && count >= 2, $"category '{category}' needs >= 2 utterances");
        }

        Assert.All(counts.Keys, category => Assert.Contains(category, AsrCorpus.RequiredCategories));
    }

    [Fact]
    public void Corpus_EveryFocusTokenAppearsVerbatimInItsText()
    {
        foreach (var item in Corpus)
        {
            foreach (var token in item.FocusTokens)
            {
                Assert.False(string.IsNullOrWhiteSpace(token), $"{item.Id} has an empty focus token");
                Assert.True(item.Text.Contains(token, StringComparison.Ordinal), $"{item.Id}: '{token}' is not in the text");
            }
        }
    }

    [Fact]
    public void Corpus_FocusTokensCoverRequiredProperNounsAndExampleForms()
    {
        var tokens = Corpus.SelectMany(item => item.FocusTokens).ToHashSet(StringComparer.Ordinal);

        foreach (var properNoun in AsrCorpus.RequiredProperNouns)
        {
            Assert.Contains(properNoun, tokens);
        }

        string[] examples = ["午後3時15分", "2026年10月1日", "1,280円", "3.5%", "API v2", "GPT-5", ".NET", "C#", "WPF", "JSON"];
        foreach (var example in examples)
        {
            Assert.Contains(example, tokens);
        }

        Assert.Contains(tokens, AsrMetrics.IsNumericToken);
        Assert.Contains(tokens, token => !AsrMetrics.IsNumericToken(token));
    }

    [Fact]
    public void Corpus_LengthAndReadingNoteCategoriesMatchTheirDefinitions()
    {
        var corpus = Corpus;

        Assert.All(corpus.Where(item => item.Category == "short"), item => Assert.True(item.Text.Length <= 8, item.Id));
        Assert.All(corpus.Where(item => item.Category == "long"), item => Assert.True(item.Text.Length >= 80, item.Id));
        Assert.All(corpus.Where(item => item.Category == "fast"), item => Assert.Equal("早口で", item.ReadingNote));
        Assert.Contains(corpus, item => item.Category == "self-correction" && item.Text.Contains("いや", StringComparison.Ordinal));
    }

    [Fact]
    public void Corpus_FileIsUtf8WithoutBomAndLf()
    {
        var bytes = File.ReadAllBytes(CorpusPath);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "corpus.json must not have a BOM");
        Assert.DoesNotContain((byte)'\r', bytes);
        _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
    }

    [Fact]
    public void Parse_RejectsItemsWithoutIdOrText()
    {
        Assert.Throws<InvalidDataException>(() => AsrCorpus.Parse("""[{"id":"","category":"general","text":"x","focusTokens":[]}]"""));
        Assert.Throws<InvalidDataException>(() => AsrCorpus.Parse("""[{"id":"u01","category":"general","text":" ","focusTokens":[]}]"""));
    }

    [Fact]
    public void Parse_MissingFocusTokensBecomesEmpty()
    {
        var item = Assert.Single(AsrCorpus.Parse("""[{"id":"u01","category":"general","text":"了解です。"}]"""));

        Assert.Empty(item.FocusTokens);
        Assert.Null(item.ReadingNote);
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

        throw new DirectoryNotFoundException("Could not locate FeatherScribe repository root.");
    }
}
