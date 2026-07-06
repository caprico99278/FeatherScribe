using System.Text.RegularExpressions;

namespace FeatherScribe.Core;

public static partial class FormatPostProcessor
{
    public static PostProcessedFormatText RemoveThinkTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PostProcessedFormatText(text, ThinkTagRemoved: false);
        }

        var cleaned = ThinkTagRegex().Replace(text, "").Trim();
        return new PostProcessedFormatText(cleaned, ThinkTagRemoved: cleaned != text.Trim());
    }

    [GeneratedRegex("<think\\b[^>]*>.*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkTagRegex();
}

public sealed record PostProcessedFormatText(string? Text, bool ThinkTagRemoved);
