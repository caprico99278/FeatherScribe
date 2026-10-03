using System.Globalization;
using System.Text;

namespace FeatherScribe.Launcher;

/// <summary>
/// Pure parsing of the console answers. Input is NFKC-normalized first, so full-width digits and letters
/// typed with the Japanese IME count like their ASCII forms. Null input (end of input) counts as Enter.
/// </summary>
internal static class LaunchPrompt
{
    /// <summary>Enter → <paramref name="defaultValue"/>, y/yes/はい → true, n/no/いいえ → false, anything else → null (ask again).</summary>
    public static bool? ParseYesNo(string? input, bool defaultValue)
        => Normalize(input) switch
        {
            "" => defaultValue,
            "y" or "yes" or "はい" => true,
            "n" or "no" or "いいえ" => false,
            _ => null,
        };

    /// <summary>The choice hint of a yes/no prompt; the capital letter is the Enter default.</summary>
    public static string YesNoHint(bool defaultValue) => defaultValue ? "[Y/n]" : "[y/N]";

    /// <summary>
    /// Index of the chosen model: a number 1..n, or Enter for the configured model (the first model when
    /// the configured one is not installed). Null when the input is invalid or there are no models.
    /// </summary>
    public static int? ParseModelChoice(string? input, IReadOnlyList<OllamaModel> models, string? configuredModel)
    {
        if (models.Count == 0)
        {
            return null;
        }

        var text = Normalize(input);
        if (text.Length == 0)
        {
            return DefaultModelIndex(models, configuredModel);
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number >= 1 && number <= models.Count
            ? number - 1
            : null;
    }

    /// <summary>The configured model's index, or 0 when it is not installed.</summary>
    public static int DefaultModelIndex(IReadOnlyList<OllamaModel> models, string? configuredModel)
        => FindModelIndex(models, configuredModel) ?? 0;

    /// <summary>
    /// Index of an installed model by tag (case-insensitive; a tag without ":" also matches its ":latest"
    /// form, as Ollama names it). Null when it is not installed.
    /// </summary>
    public static int? FindModelIndex(IReadOnlyList<OllamaModel> models, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var wanted = tag.Trim();
        var wantedLatest = wanted.Contains(':', StringComparison.Ordinal) ? null : wanted + ":latest";
        for (var index = 0; index < models.Count; index++)
        {
            var name = models[index].Name;
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase) ||
                (wantedLatest is not null && string.Equals(name, wantedLatest, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>Model size in decimal gigabytes with one decimal, e.g. "7.2 GB".</summary>
    public static string FormatSize(long sizeBytes)
        => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, sizeBytes) / 1_000_000_000d:0.0} GB");

    private static string Normalize(string? input)
        => (input ?? "").Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
}
