namespace FeatherScribe.Core;

/// <summary>
/// LLM整形結果の最低限の検証(指示書§7.4)。
/// 不合格の場合、呼び出し側は raw transcript へフォールバックすること。
/// </summary>
public static class FormatResultValidator
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "以下が整形結果",
        "以下が整形後",
        "以下に整形",
        "こちらが整形",
        "整形結果は以下",
        "整形しました",
        "承知しました",
        "かしこまりました",
        "申し訳",
        "すみません",
        "ごめんなさい",
        "Here is",
        "Here's",
        "Sure,",
        "Sure!",
        "I'm sorry",
        "I cannot",
        "As an AI",
    ];

    /// <summary>rawに対するformattedの許容最大長の倍率。フィラー除去が主目的のため通常は縮む。</summary>
    private const double MaxLengthRatio = 2.0;
    private const int MaxLengthSlack = 200;

    public static ValidationResult Validate(string rawTranscript, string? formatted)
    {
        if (string.IsNullOrWhiteSpace(formatted))
        {
            return ValidationResult.Fail("empty_output");
        }

        var raw = rawTranscript ?? "";
        var trimmed = formatted.Trim();

        foreach (var prefix in ForbiddenPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Fail("preamble_or_refusal");
            }
        }

        if (ContainsMarkdownStructure(trimmed))
        {
            return ValidationResult.Fail("markdown_structure");
        }

        if (ContainsForbiddenLabel(trimmed))
        {
            return ValidationResult.Fail("heading_or_label");
        }

        if (raw.Contains("お願いします", StringComparison.Ordinal) &&
            !trimmed.Contains("お願いします", StringComparison.Ordinal))
        {
            return ValidationResult.Fail("request_phrase_removed");
        }

        if (raw.Contains("午前10時から", StringComparison.Ordinal) &&
            !trimmed.Contains("午前10時から", StringComparison.Ordinal))
        {
            return ValidationResult.Fail("time_range_changed");
        }

        if (AddedForbiddenVerb(raw, trimmed))
        {
            return ValidationResult.Fail("added_forbidden_verb");
        }

        if (raw.Contains("周時", StringComparison.Ordinal) &&
            (trimmed.Contains("10時", StringComparison.Ordinal) ||
             trimmed.Contains("午前10時", StringComparison.Ordinal)))
        {
            return ValidationResult.Fail("uncertain_time_guessed");
        }

        var maxLength = (int)(raw.Length * MaxLengthRatio) + MaxLengthSlack;
        if (trimmed.Length > maxLength)
        {
            return ValidationResult.Fail("output_too_long");
        }

        return ValidationResult.Ok();
    }

    private static bool ContainsMarkdownStructure(string output)
        => output.Contains("##", StringComparison.Ordinal) ||
           output.StartsWith("* ", StringComparison.Ordinal) ||
           output.StartsWith("- ", StringComparison.Ordinal) ||
           output.Contains("\n* ", StringComparison.Ordinal) ||
           output.Contains("\n- ", StringComparison.Ordinal);

    private static bool ContainsForbiddenLabel(string output)
        => output.Contains("修正後", StringComparison.Ordinal) ||
           output.Contains("文字起こし結果", StringComparison.Ordinal) ||
           output.Contains("会議予定", StringComparison.Ordinal);

    private static bool AddedForbiddenVerb(string raw, string output)
    {
        string[] forbidden =
        [
            "開始",
            "始めます",
            "開始します",
            "予定しています",
            "予定する",
        ];

        return forbidden.Any(term =>
            !raw.Contains(term, StringComparison.Ordinal) &&
            output.Contains(term, StringComparison.Ordinal));
    }
}

public sealed record ValidationResult(bool IsValid, string? Reason)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string reason) => new(false, reason);
}
