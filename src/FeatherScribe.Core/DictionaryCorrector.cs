namespace FeatherScribe.Core;

/// <summary>
/// 個人辞書によるパターン置換。長いパターンから先に置換し、部分一致の巻き込みを防ぐ。
/// </summary>
public sealed class DictionaryCorrector : IDictionaryCorrector
{
    private readonly IReadOnlyList<(string Pattern, string Canonical)> _rules;

    public DictionaryCorrector(IReadOnlyList<DictionaryEntry> entries)
    {
        _rules = entries
            .SelectMany(e => e.Patterns
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => (Pattern: p, e.Canonical)))
            .OrderByDescending(r => r.Pattern.Length)
            .ToList();
    }

    public string Correct(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var (pattern, canonical) in _rules)
        {
            text = text.Replace(pattern, canonical, StringComparison.Ordinal);
        }

        return text;
    }
}
