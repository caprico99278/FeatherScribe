using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeatherScribe.Tools.AsrBenchmark;

/// <summary>
/// One utterance of the fixed ASR benchmark corpus (benchmark/asr/corpus.json).
/// </summary>
/// <param name="Id">Stable id, also the wav file name (u01 -> u01.wav).</param>
/// <param name="Category">Corpus category (general, time, date, number, ...).</param>
/// <param name="Text">Reference text, read aloud exactly as written.</param>
/// <param name="FocusTokens">Exact surface forms that must appear in the transcript.</param>
/// <param name="ReadingNote">Optional reading instruction shown when recording (e.g. "早口で"). Not part of the reference.</param>
internal sealed record CorpusItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("focusTokens")] IReadOnlyList<string> FocusTokens,
    [property: JsonPropertyName("readingNote")] string? ReadingNote = null);

internal static class AsrCorpus
{
    /// <summary>All categories the corpus must cover.</summary>
    public static readonly IReadOnlyList<string> RequiredCategories =
    [
        "general",
        "time",
        "date",
        "number",
        "punctuation",
        "self-correction",
        "fast",
        "short",
        "long",
        "alphanumeric",
        "technical",
        "proper-noun",
    ];

    /// <summary>Proper nouns that must be covered by focus tokens.</summary>
    public static readonly IReadOnlyList<string> RequiredProperNouns =
    [
        "PhoenixQuant",
        "FeatherScribe",
        "whisper.cpp",
        "Ollama",
        "Codex",
    ];

    public static IReadOnlyList<CorpusItem> Load(string path) => Parse(File.ReadAllText(path));

    public static IReadOnlyList<CorpusItem> Parse(string json)
    {
        var items = JsonSerializer.Deserialize<List<CorpusItem>>(json)
            ?? throw new InvalidDataException("Corpus JSON is empty.");

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Text))
            {
                throw new InvalidDataException("Every corpus item needs a non-empty id and text.");
            }
        }

        return items
            .Select(item => item with { FocusTokens = item.FocusTokens ?? [] })
            .ToList();
    }
}
