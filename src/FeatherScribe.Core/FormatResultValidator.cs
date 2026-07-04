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

        var trimmed = formatted.Trim();

        foreach (var prefix in ForbiddenPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Fail("preamble_or_refusal");
            }
        }

        var maxLength = (int)(rawTranscript.Length * MaxLengthRatio) + MaxLengthSlack;
        if (trimmed.Length > maxLength)
        {
            return ValidationResult.Fail("output_too_long");
        }

        return ValidationResult.Ok();
    }
}

public sealed record ValidationResult(bool IsValid, string? Reason)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string reason) => new(false, reason);
}
