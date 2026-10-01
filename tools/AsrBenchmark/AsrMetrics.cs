using System.Globalization;
using System.Text;

namespace FeatherScribe.Tools.AsrBenchmark;

/// <summary>Focus token hit counts for one transcript, split into numeric and proper-noun/tech tokens.</summary>
internal sealed record FocusTokenResult(
    int NumericHits,
    int NumericTotal,
    int ProperNounHits,
    int ProperNounTotal,
    IReadOnlyList<string> MissedTokens);

/// <summary>
/// One (model, utterance) row. Also the row shape of results.json.
/// A row with <see cref="AudioMissing"/> has no wav: it is kept in results.json (success = false,
/// Cer = null, no tokens) but excluded from every aggregate.
/// </summary>
internal sealed record UtteranceResult(
    string Model,
    string Id,
    string Category,
    string Reference,
    string Transcript,
    bool Success,
    int? ExitCode,
    string? Error,
    double LatencyMs,
    double AudioSeconds,
    long PeakWorkingSetBytes,
    double? Cer,
    int NumericHits,
    int NumericTotal,
    int ProperNounHits,
    int ProperNounTotal,
    IReadOnlyList<string> MissedFocusTokens,
    bool AudioMissing = false)
{
    /// <summary>latency / audio duration; null when the row failed or the audio length is unknown.</summary>
    public double? RealTimeFactor => Success && AudioSeconds > 0 ? LatencyMs / 1000.0 / AudioSeconds : null;
}

/// <summary>
/// Per-model aggregate of one run. <see cref="Utterances"/> is the corpus count; every metric
/// (including <see cref="Failures"/>) covers only the <see cref="Measured"/> rows that have audio.
/// MeanCer / MedianCer are null when nothing was measured.
/// </summary>
internal sealed record ModelAggregate(
    string Model,
    int Utterances,
    int Measured,
    int MissingAudio,
    int Failures,
    double? MeanCer,
    double? MedianCer,
    double? NumericAccuracy,
    double? ProperNounAccuracy,
    double? MeanLatencyMs,
    double? RealTimeFactor,
    long MaxPeakWorkingSetBytes,
    IReadOnlyDictionary<string, double> CategoryMeanCer);

/// <summary>
/// Pure ASR quality metrics (no I/O, no whisper). Unit-tested in FeatherScribe.Tests.
/// </summary>
internal static class AsrMetrics
{
    /// <summary>RTF at or below this is preferred for daily dictation.</summary>
    public const double DailyUseRealTimeFactor = 0.6;

    // Punctuation ignored by CER (checked after NFKC, so full-width forms are covered too).
    // The long vowel mark (ー) is deliberately not listed: it is a letter, not punctuation.
    private const string CerIgnoredPunctuation = "、。，．,.!?！？「」『』()（）・…";

    /// <summary>
    /// CER normalization: NFKC, remove whitespace and the punctuation set above, lowercase ASCII.
    /// </summary>
    public static string NormalizeForCer(string text)
    {
        var nfkc = (text ?? "").Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(nfkc.Length);
        foreach (var ch in nfkc)
        {
            if (char.IsWhiteSpace(ch) || CerIgnoredPunctuation.Contains(ch))
            {
                continue;
            }

            builder.Append(ch is >= 'A' and <= 'Z' ? (char)(ch + ('a' - 'A')) : ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Levenshtein(normRef, normHyp) / len(normRef), counted in Unicode scalar values.
    /// 0 when both are empty; 1 when only the reference is empty.
    /// </summary>
    public static double CharacterErrorRate(string reference, string hypothesis)
    {
        var normalizedReference = ToScalars(NormalizeForCer(reference));
        var normalizedHypothesis = ToScalars(NormalizeForCer(hypothesis));

        if (normalizedReference.Length == 0)
        {
            return normalizedHypothesis.Length == 0 ? 0.0 : 1.0;
        }

        return (double)Levenshtein(normalizedReference, normalizedHypothesis) / normalizedReference.Length;
    }

    /// <summary>Edit distance with unit cost for substitution, insertion and deletion.</summary>
    public static int Levenshtein(IReadOnlyList<int> source, IReadOnlyList<int> target)
    {
        if (source.Count == 0)
        {
            return target.Count;
        }

        if (target.Count == 0)
        {
            return source.Count;
        }

        var previous = new int[target.Count + 1];
        var current = new int[target.Count + 1];
        for (var j = 0; j <= target.Count; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= source.Count; i++)
        {
            current[0] = i;
            for (var j = 1; j <= target.Count; j++)
            {
                var substitution = previous[j - 1] + (source[i - 1] == target[j - 1] ? 0 : 1);
                var deletion = previous[j] + 1;
                var insertion = current[j - 1] + 1;
                current[j] = Math.Min(substitution, Math.Min(deletion, insertion));
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Count];
    }

    /// <summary>A focus token is numeric (number/time/date/amount) when it contains a digit after NFKC.</summary>
    public static bool IsNumericToken(string token)
        => (token ?? "").Normalize(NormalizationForm.FormKC).Any(char.IsAsciiDigit);

    /// <summary>
    /// Counts focus tokens found in the hypothesis. Matching is NFKC, case-insensitive and
    /// whitespace-insensitive substring search; numeric tokens are counted separately from
    /// proper nouns / technical terms.
    /// </summary>
    public static FocusTokenResult FocusTokenHits(IEnumerable<string> focusTokens, string hypothesis)
    {
        var normalizedHypothesis = NormalizeForTokenMatch(hypothesis);
        int numericHits = 0, numericTotal = 0, properHits = 0, properTotal = 0;
        var missed = new List<string>();

        foreach (var token in focusTokens)
        {
            var normalizedToken = NormalizeForTokenMatch(token);
            if (normalizedToken.Length == 0)
            {
                continue;
            }

            var hit = normalizedHypothesis.Contains(normalizedToken, StringComparison.Ordinal);
            if (IsNumericToken(token))
            {
                numericTotal++;
                numericHits += hit ? 1 : 0;
            }
            else
            {
                properTotal++;
                properHits += hit ? 1 : 0;
            }

            if (!hit)
            {
                missed.Add(token);
            }
        }

        return new FocusTokenResult(numericHits, numericTotal, properHits, properTotal, missed);
    }

    /// <summary>Median of the values (mean of the middle two for an even count); 0 for an empty list.</summary>
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0.0;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>
    /// Aggregates one model's rows. Rows without audio (<see cref="UtteranceResult.AudioMissing"/>)
    /// are excluded from every metric, from Failures and from per-category CER.
    /// Over the measured rows, quality metrics (CER, token accuracy) include failed rows
    /// (their transcript is empty, so CER = 1 and no token hits); speed metrics (latency, RTF)
    /// use successful rows only. Token accuracy is micro-averaged (total hits / total tokens).
    /// RTF = total latency / total audio duration.
    /// </summary>
    public static ModelAggregate Aggregate(string model, IReadOnlyList<UtteranceResult> rows)
    {
        var measured = rows.Where(r => !r.AudioMissing).ToList();
        var successful = measured.Where(r => r.Success).ToList();
        var numericTotal = measured.Sum(r => r.NumericTotal);
        var properTotal = measured.Sum(r => r.ProperNounTotal);
        var successfulAudioSeconds = successful.Sum(r => r.AudioSeconds);

        return new ModelAggregate(
            Model: model,
            Utterances: rows.Count,
            Measured: measured.Count,
            MissingAudio: rows.Count - measured.Count,
            Failures: measured.Count - successful.Count,
            MeanCer: measured.Count == 0 ? null : measured.Average(RowCer),
            MedianCer: measured.Count == 0 ? null : Median(measured.Select(RowCer)),
            NumericAccuracy: numericTotal == 0 ? null : (double)measured.Sum(r => r.NumericHits) / numericTotal,
            ProperNounAccuracy: properTotal == 0 ? null : (double)measured.Sum(r => r.ProperNounHits) / properTotal,
            MeanLatencyMs: successful.Count == 0 ? null : successful.Average(r => r.LatencyMs),
            RealTimeFactor: successfulAudioSeconds > 0
                ? successful.Sum(r => r.LatencyMs) / 1000.0 / successfulAudioSeconds
                : null,
            MaxPeakWorkingSetBytes: measured.Count == 0 ? 0 : measured.Max(r => r.PeakWorkingSetBytes),
            CategoryMeanCer: measured
                .GroupBy(r => r.Category, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Average(RowCer), StringComparer.Ordinal));
    }

    // A measured row always carries a CER; a failed row was scored against an empty transcript (CER 1).
    private static double RowCer(UtteranceResult row) => row.Cer ?? 1.0;

    /// <summary>
    /// Advisory decision-helper order: fewer failures, then higher numeric + proper-noun accuracy,
    /// then lower mean CER, then RTF within the daily-use limit, then lower mean latency.
    /// The decision itself is taken by a human (docs/research/asr_model_benchmark.md).
    /// </summary>
    public static IReadOnlyList<ModelAggregate> RankForDecision(IEnumerable<ModelAggregate> aggregates)
        => aggregates
            .OrderBy(a => a.Failures)
            .ThenByDescending(a => (a.NumericAccuracy ?? 0.0) + (a.ProperNounAccuracy ?? 0.0))
            .ThenBy(a => a.MeanCer ?? double.MaxValue)
            .ThenBy(a => a.RealTimeFactor is { } rtf && rtf <= DailyUseRealTimeFactor ? 0 : 1)
            .ThenBy(a => a.MeanLatencyMs ?? double.MaxValue)
            .ThenBy(a => a.Model, StringComparer.Ordinal)
            .ToList();

    /// <summary>Scores one transcript against its corpus item.</summary>
    public static (double Cer, FocusTokenResult Focus) Score(CorpusItem item, string transcript)
        => (CharacterErrorRate(item.Text, transcript), FocusTokenHits(item.FocusTokens, transcript));

    private static string NormalizeForTokenMatch(string text)
    {
        var nfkc = (text ?? "").Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(nfkc.Length);
        foreach (var ch in nfkc)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static int[] ToScalars(string text)
    {
        var scalars = new List<int>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            scalars.Add(rune.Value);
        }

        return [.. scalars];
    }

    /// <summary>Invariant "0.123" style formatting for reports.</summary>
    public static string Format(double? value, string format = "0.000")
        => value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : "n/a";
}
