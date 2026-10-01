using FeatherScribe.Tools.AsrBenchmark;

namespace FeatherScribe.Tests;

public class AsrMetricsTests
{
    [Fact]
    public void Cer_IdenticalText_IsZero()
    {
        Assert.Equal(0.0, AsrMetrics.CharacterErrorRate("会議は午後3時15分から始まります。", "会議は午後3時15分から始まります。"));
    }

    [Fact]
    public void Cer_OneSubstitution_IsOneOverReferenceLength()
    {
        Assert.Equal(0.2, AsrMetrics.CharacterErrorRate("あいうえお", "あいかえお"), 10);
    }

    [Fact]
    public void Cer_OneInsertion_IsOneOverReferenceLength()
    {
        Assert.Equal(0.2, AsrMetrics.CharacterErrorRate("あいうえお", "あいうえおか"), 10);
    }

    [Fact]
    public void Cer_OneDeletion_IsOneOverReferenceLength()
    {
        Assert.Equal(0.2, AsrMetrics.CharacterErrorRate("あいうえお", "あいえお"), 10);
    }

    [Theory]
    [InlineData("今日は、晴れです。", "今日は晴れです")]
    [InlineData("本当ですか？「はい」", "本当ですか!はい")]
    [InlineData("ＡＰＩ　ｖ２のテスト", "API v2 のテスト")]
    [InlineData("１，２８０円", "1280円")]
    [InlineData("WPF・JSON（設定）…", "wpf json 設定")]
    [InlineData("『ｗｈｉｓｐｅｒ．ｃｐｐ』", "whisper cpp")]
    public void Cer_IgnoresPunctuationWhitespaceWidthAndAsciiCase(string reference, string hypothesis)
    {
        Assert.Equal(0.0, AsrMetrics.CharacterErrorRate(reference, hypothesis));
    }

    [Fact]
    public void Cer_KeepsLongVowelMark()
    {
        Assert.Equal("ケーブル", AsrMetrics.NormalizeForCer("ケーブル。"));
        Assert.Equal(0.25, AsrMetrics.CharacterErrorRate("ケーブル", "ケブル"), 10);
    }

    [Fact]
    public void Cer_EmptyCases()
    {
        Assert.Equal(0.0, AsrMetrics.CharacterErrorRate("", ""));
        Assert.Equal(0.0, AsrMetrics.CharacterErrorRate("。", " "));
        Assert.Equal(1.0, AsrMetrics.CharacterErrorRate("", "余計"));
        Assert.Equal(1.0, AsrMetrics.CharacterErrorRate("あいう", ""));
    }

    [Fact]
    public void Cer_CountsSurrogatePairAsOneCharacter()
    {
        Assert.Equal(1.0 / 3.0, AsrMetrics.CharacterErrorRate("\U00020BB7野家", "吉野家"), 10);
    }

    [Fact]
    public void FocusTokenHits_SplitsNumericAndProperNounTokens()
    {
        var result = AsrMetrics.FocusTokenHits(
            ["午後3時15分", "PhoenixQuant", "3.5%", "Ollama"],
            "午後3時15分に phoenix quant で3.5パーセント");

        Assert.Equal(1, result.NumericHits);
        Assert.Equal(2, result.NumericTotal);
        Assert.Equal(1, result.ProperNounHits);
        Assert.Equal(2, result.ProperNounTotal);
        Assert.Equal(["3.5%", "Ollama"], result.MissedTokens);
    }

    [Theory]
    [InlineData("1,280円", "このケーブルは１，２８０円でした")]
    [InlineData("GPT-5", "ｇｐｔ－５の出力")]
    [InlineData("API v2", "新しいapiv2では")]
    [InlineData(".NET", "．ｎｅｔとC#")]
    [InlineData("whisper.cpp", "Whisper.CPPで")]
    public void FocusTokenHits_MatchesAfterNfkcCaseAndWhitespaceNormalization(string token, string hypothesis)
    {
        var result = AsrMetrics.FocusTokenHits([token], hypothesis);

        Assert.Empty(result.MissedTokens);
        Assert.Equal(1, result.NumericHits + result.ProperNounHits);
    }

    [Fact]
    public void FocusTokenHits_DifferentSurfaceFormIsAMiss()
    {
        var result = AsrMetrics.FocusTokenHits(["1,280円", "Codex"], "1280円でコーデックス");

        Assert.Equal(0, result.NumericHits);
        Assert.Equal(0, result.ProperNounHits);
        Assert.Equal(["1,280円", "Codex"], result.MissedTokens);
    }

    [Theory]
    [InlineData("午後3時15分", true)]
    [InlineData("１０時", true)]
    [InlineData("GPT-5", true)]
    [InlineData("C#", false)]
    [InlineData("佐藤", false)]
    public void IsNumericToken_IsTrueWhenTheTokenContainsADigit(string token, bool expected)
    {
        Assert.Equal(expected, AsrMetrics.IsNumericToken(token));
    }

    [Fact]
    public void Median_HandlesOddEvenAndEmpty()
    {
        Assert.Equal(2.0, AsrMetrics.Median([3.0, 1.0, 2.0]));
        Assert.Equal(2.5, AsrMetrics.Median([4.0, 1.0, 2.0, 3.0]));
        Assert.Equal(0.0, AsrMetrics.Median([]));
    }

    [Fact]
    public void Aggregate_ComputesQualityOverAllRowsAndSpeedOverSuccessfulRows()
    {
        UtteranceResult[] rows =
        [
            Row("u01", "time", success: true, cer: 0.0, latencyMs: 1000, audioSeconds: 2, peak: 100, numeric: (1, 1)),
            Row("u02", "time", success: true, cer: 0.5, latencyMs: 2000, audioSeconds: 4, peak: 300, numeric: (0, 1)),
            Row("u03", "proper-noun", success: false, cer: 1.0, latencyMs: 9000, audioSeconds: 3, peak: 0, numeric: (0, 1), proper: (0, 1)),
            Row("u04", "proper-noun", success: true, cer: 0.1, latencyMs: 3000, audioSeconds: 4, peak: 200, proper: (1, 1)),
        ];

        var aggregate = AsrMetrics.Aggregate("ggml-test", rows);

        Assert.Equal("ggml-test", aggregate.Model);
        Assert.Equal(4, aggregate.Utterances);
        Assert.Equal(4, aggregate.Measured);
        Assert.Equal(0, aggregate.MissingAudio);
        Assert.Equal(1, aggregate.Failures);
        Assert.Equal(0.4, aggregate.MeanCer!.Value, 10);
        Assert.Equal(0.3, aggregate.MedianCer!.Value, 10);
        Assert.Equal(1.0 / 3.0, aggregate.NumericAccuracy!.Value, 10);
        Assert.Equal(0.5, aggregate.ProperNounAccuracy!.Value, 10);
        Assert.Equal(2000.0, aggregate.MeanLatencyMs!.Value, 10);
        Assert.Equal(0.6, aggregate.RealTimeFactor!.Value, 10);
        Assert.Equal(300, aggregate.MaxPeakWorkingSetBytes);
        Assert.Equal(0.25, aggregate.CategoryMeanCer["time"], 10);
        Assert.Equal(0.55, aggregate.CategoryMeanCer["proper-noun"], 10);
    }

    [Fact]
    public void Aggregate_WithoutFocusTokensOrSuccesses_ReportsNotAvailable()
    {
        var aggregate = AsrMetrics.Aggregate(
            "ggml-test",
            [Row("u01", "general", success: false, cer: 1.0, latencyMs: 0, audioSeconds: 0, peak: 0)]);

        Assert.Null(aggregate.NumericAccuracy);
        Assert.Null(aggregate.ProperNounAccuracy);
        Assert.Null(aggregate.MeanLatencyMs);
        Assert.Null(aggregate.RealTimeFactor);
        Assert.Equal(1, aggregate.Failures);
    }

    [Fact]
    public void Aggregate_ExcludesMissingAudioRowsFromEveryMetric()
    {
        UtteranceResult[] measured =
        [
            Row("u01", "time", success: true, cer: 0.0, latencyMs: 1000, audioSeconds: 2, peak: 100, numeric: (1, 1)),
            Row("u02", "time", success: true, cer: 0.5, latencyMs: 2000, audioSeconds: 4, peak: 300, numeric: (0, 1)),
            Row("u03", "proper-noun", success: false, cer: 1.0, latencyMs: 9000, audioSeconds: 3, peak: 0, numeric: (0, 1), proper: (0, 1)),
            Row("u04", "proper-noun", success: true, cer: 0.1, latencyMs: 3000, audioSeconds: 4, peak: 200, proper: (1, 1)),
        ];
        UtteranceResult[] withMissing =
        [
            .. measured,
            MissingRow("u05", "time"),
            MissingRow("u06", "long"),
        ];

        var baseline = AsrMetrics.Aggregate("ggml-test", measured);
        var aggregate = AsrMetrics.Aggregate("ggml-test", withMissing);

        Assert.Equal(6, aggregate.Utterances);
        Assert.Equal(4, aggregate.Measured);
        Assert.Equal(2, aggregate.MissingAudio);
        Assert.Equal(1, aggregate.Failures);
        Assert.Equal(baseline.MeanCer, aggregate.MeanCer);
        Assert.Equal(baseline.MedianCer, aggregate.MedianCer);
        Assert.Equal(baseline.NumericAccuracy, aggregate.NumericAccuracy);
        Assert.Equal(baseline.ProperNounAccuracy, aggregate.ProperNounAccuracy);
        Assert.Equal(baseline.MeanLatencyMs, aggregate.MeanLatencyMs);
        Assert.Equal(baseline.RealTimeFactor, aggregate.RealTimeFactor);
        Assert.Equal(baseline.MaxPeakWorkingSetBytes, aggregate.MaxPeakWorkingSetBytes);
        Assert.Equal(baseline.CategoryMeanCer, aggregate.CategoryMeanCer);
        Assert.False(aggregate.CategoryMeanCer.ContainsKey("long"));
    }

    [Fact]
    public void Aggregate_AllAudioMissing_HasNoMetricsAndNoFailures()
    {
        var aggregate = AsrMetrics.Aggregate("ggml-test", [MissingRow("u01", "time"), MissingRow("u02", "date")]);

        Assert.Equal(2, aggregate.Utterances);
        Assert.Equal(0, aggregate.Measured);
        Assert.Equal(2, aggregate.MissingAudio);
        Assert.Equal(0, aggregate.Failures);
        Assert.Null(aggregate.MeanCer);
        Assert.Null(aggregate.MedianCer);
        Assert.Null(aggregate.NumericAccuracy);
        Assert.Null(aggregate.ProperNounAccuracy);
        Assert.Null(aggregate.MeanLatencyMs);
        Assert.Null(aggregate.RealTimeFactor);
        Assert.Empty(aggregate.CategoryMeanCer);
    }

    [Fact]
    public void Report_PartialAudioSet_ShowsHeaderMeasuredColumnAndPartialWarning()
    {
        UtteranceResult[] rows =
        [
            Row("u01", "time", success: true, cer: 0.1, latencyMs: 1000, audioSeconds: 2, peak: 0),
            Row("u02", "general", success: false, cer: 1.0, latencyMs: 500, audioSeconds: 2, peak: 0) with { Error = "exit code 3: boom" },
            MissingRow("u03", "long"),
            MissingRow("u04", "long"),
            MissingRow("u05", "long"),
            MissingRow("u07", "date"),
        ];
        var aggregate = AsrMetrics.Aggregate("ggml-test", rows);

        var report = BenchmarkReport.BuildMarkdown(Metadata(AudioSetKind.Real, 6), [aggregate], rows);

        Assert.Contains(
            "> **Partial audio set:** 2 of 6 utterances have audio. Metrics cover only those; missing: u03–u05, u07.",
            report);
        Assert.Contains("| Model | Measured |", report);
        Assert.Contains("| ggml-test | 2/6 |", report);
        Assert.Contains("| 1/2 |", report); // failures are counted over measured rows only
        Assert.Contains("Partial set: the ranking covers 2 utterances; record the rest before locking the model.", report);
        Assert.Contains("| long | n/a |", report);
        Assert.Contains("| date | n/a |", report);

        var failures = report[report.IndexOf("## Failures", StringComparison.Ordinal)..];
        Assert.Contains("ggml-test u02", failures);
        Assert.DoesNotContain("u03", failures);
        Assert.DoesNotContain("u07", failures);
        Assert.DoesNotContain("audio missing", report);
        Assert.DoesNotContain("10時", report); // tokens of missing rows are not "missed"
    }

    [Fact]
    public void Report_CompleteAudioSet_HasNoPartialWarnings()
    {
        UtteranceResult[] rows = [Row("u01", "time", success: true, cer: 0.1, latencyMs: 1000, audioSeconds: 2, peak: 0)];

        var report = BenchmarkReport.BuildMarkdown(
            Metadata(AudioSetKind.Real, 1), [AsrMetrics.Aggregate("ggml-test", rows)], rows);

        Assert.DoesNotContain("Partial", report);
        Assert.DoesNotContain("## Failures", report);
        Assert.Contains("| ggml-test | 1/1 |", report);
    }

    [Theory]
    [InlineData(new[] { "u15", "u16", "u17", "u18" }, "u15–u18")]
    [InlineData(new[] { "u01", "u02", "u05" }, "u01, u02, u05")]
    [InlineData(new[] { "u08", "u09", "u10", "u12" }, "u08–u10, u12")]
    [InlineData(new[] { "intro", "u01" }, "intro, u01")]
    public void CompactIds_UsesRangesForThreeOrMoreConsecutiveIds(string[] ids, string expected)
    {
        Assert.Equal(expected, BenchmarkReport.CompactIds(ids));
    }

    [Fact]
    public void UtteranceResult_RealTimeFactorIsLatencyOverAudioDuration()
    {
        Assert.Equal(0.5, Row("u01", "general", success: true, cer: 0, latencyMs: 1500, audioSeconds: 3, peak: 0).RealTimeFactor!.Value, 10);
        Assert.Null(Row("u01", "general", success: false, cer: 1, latencyMs: 1500, audioSeconds: 3, peak: 0).RealTimeFactor);
    }

    [Fact]
    public void RankForDecision_OrdersByFailuresAccuracyCerThenDailyUseLatency()
    {
        var failing = Aggregate("failing", failures: 1, numeric: 1.0, proper: 1.0, cer: 0.01, rtf: 0.1, latency: 100);
        var lessAccurate = Aggregate("less-accurate", failures: 0, numeric: 0.5, proper: 0.5, cer: 0.01, rtf: 0.1, latency: 100);
        var slow = Aggregate("slow", failures: 0, numeric: 1.0, proper: 0.5, cer: 0.10, rtf: 0.9, latency: 1000);
        var dailyUse = Aggregate("daily-use", failures: 0, numeric: 1.0, proper: 0.5, cer: 0.10, rtf: 0.5, latency: 3000);
        var lowerCer = Aggregate("lower-cer", failures: 0, numeric: 1.0, proper: 0.5, cer: 0.05, rtf: 2.0, latency: 9000);

        var ranked = AsrMetrics.RankForDecision([failing, lessAccurate, slow, dailyUse, lowerCer]);

        Assert.Equal(
            ["lower-cer", "daily-use", "slow", "less-accurate", "failing"],
            ranked.Select(a => a.Model));
    }

    [Fact]
    public void Report_MarksSyntheticRunsAndNeverContainsTranscripts()
    {
        var row = Row("u01", "general", success: true, cer: 0.1, latencyMs: 1000, audioSeconds: 2, peak: 1024 * 1024) with
        {
            Transcript = "ひみつの書き起こし本文",
        };
        var aggregate = AsrMetrics.Aggregate("ggml-small", [row]);
        var metadata = new RunMetadata(
            DateTimeOffset.Now, AudioSetKind.Synthetic, "System.Speech TTS", 1, "whisper-cli.exe", "ja", 4, 300,
            ["ggml-small.bin"], 8, "Windows");

        var report = BenchmarkReport.BuildMarkdown(metadata, [aggregate], [row]);

        Assert.Contains("SYNTHETIC AUDIO", report);
        Assert.Contains("must not be used to lock", report);
        Assert.Contains("| ggml-small |", report);
        Assert.DoesNotContain("ひみつの書き起こし本文", report);
        Assert.DoesNotContain("\r", report);
    }

    private static UtteranceResult Row(
        string id,
        string category,
        bool success,
        double cer,
        double latencyMs,
        double audioSeconds,
        long peak,
        (int Hits, int Total) numeric = default,
        (int Hits, int Total) proper = default)
        => new(
            "ggml-test", id, category, "reference", success ? "hypothesis" : "", success,
            success ? 0 : 1, success ? null : "error", latencyMs, audioSeconds, peak, cer,
            numeric.Hits, numeric.Total, proper.Hits, proper.Total, []);

    private static ModelAggregate Aggregate(
        string model, int failures, double numeric, double proper, double cer, double rtf, double latency)
        => new(model, 30, 30, 0, failures, cer, cer, numeric, proper, latency, rtf, 0, new Dictionary<string, double>());

    private static UtteranceResult MissingRow(string id, string category)
        => new(
            "ggml-test", id, category, "reference", "", Success: false, ExitCode: null, Error: "audio missing",
            LatencyMs: 0, AudioSeconds: 0, PeakWorkingSetBytes: 0, Cer: null,
            NumericHits: 0, NumericTotal: 1, ProperNounHits: 0, ProperNounTotal: 1, MissedFocusTokens: ["10時"],
            AudioMissing: true);

    private static RunMetadata Metadata(string audioKind, int corpusUtterances)
        => new(
            DateTimeOffset.Now, audioKind, null, corpusUtterances, "whisper-cli.exe", "ja", 4, 300,
            ["ggml-test.bin"], 8, "Windows");
}
