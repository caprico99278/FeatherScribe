using System.Text;

namespace FeatherScribe.Core;

/// <summary>
/// プロンプトテンプレートの {{dictionary}} / {{raw_transcript}} を実値へ展開する。
/// </summary>
public static class PromptBuilder
{
    public const string DictionaryPlaceholder = "{{dictionary}}";
    public const string TranscriptPlaceholder = "{{raw_transcript}}";

    public static string Build(
        string template,
        IReadOnlyList<DictionaryEntry> dictionaryEntries,
        string rawTranscript)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(rawTranscript);

        return template
            .Replace(DictionaryPlaceholder, BuildDictionarySection(dictionaryEntries))
            .Replace(TranscriptPlaceholder, rawTranscript);
    }

    public static string BuildDictionarySection(IReadOnlyList<DictionaryEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return "(辞書なし)";
        }

        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder
                .Append("- ")
                .Append(string.Join(", ", entry.Patterns))
                .Append(" => ")
                .AppendLine(entry.Canonical);
        }

        return builder.ToString().TrimEnd('\n', '\r');
    }
}
