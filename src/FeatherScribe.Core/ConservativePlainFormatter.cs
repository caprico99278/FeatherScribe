using System.Text.RegularExpressions;

namespace FeatherScribe.Core;

/// <summary>
/// LLM出力を採用できない場合の、意味を変えない最小限の整形。
/// 数字・時刻・固有名詞・文末表現は推測で変更しない。
/// </summary>
public static partial class ConservativePlainFormatter
{
    public static string Format(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return "";
        }

        var text = WhitespaceRegex().Replace(rawText.Trim(), " ");
        text = LeadingFillerRegex().Replace(text, "");
        text = text.Replace("、、", "、", StringComparison.Ordinal)
            .Replace("。。", "。", StringComparison.Ordinal);

        text = InsertCommaBeforeMazURegex().Replace(text, "は、まず");

        if (!EndsWithSentencePunctuation(text))
        {
            text += "。";
        }

        return text;
    }

    private static bool EndsWithSentencePunctuation(string text)
        => text.EndsWith('。') ||
           text.EndsWith('！') ||
           text.EndsWith('？') ||
           text.EndsWith('!') ||
           text.EndsWith('?');

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(えーと|えっと|あの|その|まあ|まー)[、,\s]*")]
    private static partial Regex LeadingFillerRegex();

    [GeneratedRegex(@"は[、,\s]*まず")]
    private static partial Regex InsertCommaBeforeMazURegex();
}
