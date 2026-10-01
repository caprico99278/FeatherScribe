using System.Globalization;
using System.Text;

namespace FeatherScribe.Tools.AsrBenchmark;

/// <summary>Kinds of audio set; recorded in audioset.json next to the wavs and in every run.</summary>
internal static class AudioSetKind
{
    public const string Synthetic = "synthetic";
    public const string Real = "real";
    public const string Unknown = "unknown";
}

/// <summary>Run metadata written to results.json and summarized at the top of report.md.</summary>
internal sealed record RunMetadata(
    DateTimeOffset CreatedAt,
    string AudioKind,
    string? AudioSource,
    int CorpusUtterances,
    string WhisperExecutable,
    string Language,
    int Threads,
    int TimeoutSeconds,
    IReadOnlyList<string> Models,
    int LogicalProcessors,
    string OsVersion);

/// <summary>Builds report.md from aggregates. Pure; never includes transcripts.</summary>
internal static class BenchmarkReport
{
    public static string BuildMarkdown(
        RunMetadata metadata,
        IReadOnlyList<ModelAggregate> aggregates,
        IReadOnlyList<UtteranceResult> rows)
    {
        var missingIds = rows
            .Where(r => r.AudioMissing)
            .Select(r => r.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var measuredCount = metadata.CorpusUtterances - missingIds.Count;

        var md = new StringBuilder();
        md.AppendLine("# ASR benchmark report");
        md.AppendLine();
        md.AppendLine(AudioKindBanner(metadata.AudioKind));
        md.AppendLine();
        if (missingIds.Count > 0)
        {
            md.AppendLine(
                $"> **Partial audio set:** {measuredCount} of {metadata.CorpusUtterances} utterances have audio. " +
                $"Metrics cover only those; missing: {CompactIds(missingIds)}.");
            md.AppendLine();
        }

        md.AppendLine($"- Created: {metadata.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        md.AppendLine($"- Audio set: `{metadata.AudioKind}`" + (metadata.AudioSource is null ? "" : $" ({metadata.AudioSource})"));
        md.AppendLine($"- Corpus utterances: {metadata.CorpusUtterances}");
        md.AppendLine($"- whisper-cli: `{Path.GetFileName(metadata.WhisperExecutable)}`, language `{metadata.Language}`, threads {metadata.Threads}, timeout {metadata.TimeoutSeconds}s (production arguments via WhisperCppCommandBuilder)");
        md.AppendLine($"- Machine: {metadata.LogicalProcessors} logical processors, {metadata.OsVersion}");
        md.AppendLine("- Transcripts are only in results.json (local, gitignored). This report contains aggregates only.");
        md.AppendLine();

        md.AppendLine("## Models");
        md.AppendLine();
        md.AppendLine("| Model | Measured | Mean CER | Median CER | Numeric acc. | Proper-noun acc. | Mean latency (ms) | RTF | Peak memory (MB) | Failures |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var a in aggregates)
        {
            md.AppendLine(
                $"| {a.Model} | {a.Measured}/{a.Utterances} | {AsrMetrics.Format(a.MeanCer)} | {AsrMetrics.Format(a.MedianCer)} | " +
                $"{Percent(a.NumericAccuracy)} | {Percent(a.ProperNounAccuracy)} | " +
                $"{AsrMetrics.Format(a.MeanLatencyMs, "0")} | {AsrMetrics.Format(a.RealTimeFactor, "0.00")} | " +
                $"{AsrMetrics.Format(a.MaxPeakWorkingSetBytes / 1024.0 / 1024.0, "0")} | {a.Failures}/{a.Measured} |");
        }

        md.AppendLine();
        md.AppendLine("## CER by category (mean)");
        md.AppendLine();
        md.AppendLine("| Category | " + string.Join(" | ", aggregates.Select(a => a.Model)) + " |");
        md.AppendLine("|---|" + string.Concat(aggregates.Select(_ => "---:|")));
        // Categories come from all rows, so a category with no measured audio still shows (as n/a).
        var categories = rows
            .Select(r => r.Category)
            .Concat(aggregates.SelectMany(a => a.CategoryMeanCer.Keys))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var category in categories)
        {
            md.AppendLine(
                $"| {category} | " +
                string.Join(" | ", aggregates.Select(a =>
                    a.CategoryMeanCer.TryGetValue(category, out var cer) ? AsrMetrics.Format(cer) : "n/a")) +
                " |");
        }

        md.AppendLine();
        md.AppendLine("## Decision helper (advisory)");
        md.AppendLine();
        md.AppendLine(
            "Order: failures (fewer) > numeric + proper-noun accuracy (higher) > mean CER (lower) > " +
            $"RTF <= {AsrMetrics.DailyUseRealTimeFactor.ToString("0.0", CultureInfo.InvariantCulture)} preferred > mean latency (lower).");
        md.AppendLine();
        var ranked = AsrMetrics.RankForDecision(aggregates);
        md.AppendLine("Ranking: " + string.Join(" > ", ranked.Select(a =>
            a.RealTimeFactor is { } rtf && rtf <= AsrMetrics.DailyUseRealTimeFactor
                ? a.Model
                : $"{a.Model} (RTF > {AsrMetrics.DailyUseRealTimeFactor.ToString("0.0", CultureInfo.InvariantCulture)})")));
        md.AppendLine();
        md.AppendLine(metadata.AudioKind == AudioSetKind.Real
            ? "The ranking is advisory. The decision is documented by a human in docs/research/asr_model_benchmark.md."
            : "This run is NOT real-voice: the ranking only validates the tooling and must not be used to lock the model.");
        if (measuredCount < metadata.CorpusUtterances)
        {
            md.AppendLine();
            md.AppendLine(
                $"Partial set: the ranking covers {measuredCount} utterances; record the rest before locking the model.");
        }

        md.AppendLine();
        md.AppendLine("## Missed focus tokens");
        md.AppendLine();
        foreach (var a in aggregates)
        {
            var missed = rows
                .Where(r => r.Model == a.Model && !r.AudioMissing && r.MissedFocusTokens.Count > 0)
                .Select(r => $"{r.Id}: {string.Join(", ", r.MissedFocusTokens)}")
                .ToList();
            md.AppendLine($"- {a.Model}: " + (missed.Count == 0 ? "none" : string.Join("; ", missed)));
        }

        // Missing audio is reported in the partial-set header, not as a failure.
        var failures = rows.Where(r => !r.Success && !r.AudioMissing).ToList();
        if (failures.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("## Failures");
            md.AppendLine();
            foreach (var failure in failures)
            {
                md.AppendLine($"- {failure.Model} {failure.Id}: exit {failure.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}, {OneLine(failure.Error)}");
            }
        }

        return md.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string AudioKindBanner(string audioKind) => audioKind switch
    {
        AudioSetKind.Synthetic =>
            "> **SYNTHETIC AUDIO (TTS).** Tooling validation only. These numbers must not be used to lock the production model.",
        AudioSetKind.Real =>
            "> Real-voice audio set (user recordings).",
        _ =>
            "> **Audio set kind is unknown** (no audioset.json in the audio folder). Treat as not real-voice.",
    };

    /// <summary>
    /// Lists ids compactly: runs of 3+ consecutive ids with the same prefix and digit width become
    /// "u15–u30"; everything else is a comma list. Order is kept as given.
    /// </summary>
    public static string CompactIds(IReadOnlyList<string> ids)
    {
        var parts = new List<string>();
        var index = 0;
        while (index < ids.Count)
        {
            var end = index;
            while (end + 1 < ids.Count && IsNextId(ids[end], ids[end + 1]))
            {
                end++;
            }

            if (end - index >= 2)
            {
                parts.Add($"{ids[index]}–{ids[end]}");
            }
            else
            {
                for (var i = index; i <= end; i++)
                {
                    parts.Add(ids[i]);
                }
            }

            index = end + 1;
        }

        return string.Join(", ", parts);
    }

    private static bool IsNextId(string current, string next)
        => SplitId(current) is { } a
            && SplitId(next) is { } b
            && a.Prefix == b.Prefix
            && a.Digits.Length == b.Digits.Length
            && long.Parse(b.Digits, CultureInfo.InvariantCulture) == long.Parse(a.Digits, CultureInfo.InvariantCulture) + 1;

    private static (string Prefix, string Digits)? SplitId(string id)
    {
        var digitStart = id.Length;
        while (digitStart > 0 && char.IsAsciiDigit(id[digitStart - 1]))
        {
            digitStart--;
        }

        var digits = id[digitStart..];
        return digits.Length is > 0 and <= 18 ? (id[..digitStart], digits) : null;
    }

    private static string Percent(double? value)
        => value is { } v ? (v * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "n/a";

    private static string OneLine(string? text)
    {
        var line = (text ?? "").ReplaceLineEndings(" ").Trim();
        return line.Length > 200 ? line[..200] + "..." : line;
    }
}
