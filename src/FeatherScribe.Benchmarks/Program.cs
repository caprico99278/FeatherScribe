using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Benchmarks;

internal static class Program
{
    private const int RequiredTermDisqualificationThreshold = 2;

    private const string DefaultModels =
        "qwen3:0.6b,gemma3:1b,qwen3:1.7b,hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M,qwen3:4b,llama3.2:3b,phi4-mini:3.8b,gemma3:4b,gemma4:e2b,gemma4:e4b";

    private static readonly string[] RequiredTerms =
    [
        "PhoenixQuant",
        "FeatherScribe",
        "Codex",
        "whisper.cpp",
        "Gemma 4",
        "OperationId",
        "DictationPipeline",
    ];

    public static async Task<int> Main(string[] args)
    {
        var options = BenchmarkOptions.Parse(args);
        Directory.CreateDirectory(options.OutputDirectory);

        var samples = LoadSamples(options.InputDirectory);
        if (samples.Count == 0)
        {
            Console.Error.WriteLine($"No benchmark input files found: {options.InputDirectory}");
            return 1;
        }

        var template = File.ReadAllText(options.PromptPath, Encoding.UTF8);
        template += """

            Benchmark-only constraints:
            - Output only the formatted text.
            - Do not add explanations, headings, bullet markers, or Markdown unless the input explicitly asks for bullets.
            - Do not output <think> tags or reasoning.
            - Do not add facts that are not present in the input.
            """;

        using var httpClient = new HttpClient { BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/") };
        var results = new List<BenchmarkResult>();
        var outputBlocks = new List<OutputBlock>();

        foreach (var model in options.Models)
        {
            Console.WriteLine($"model: {model}");
            foreach (var sample in samples)
            {
                Console.WriteLine($"  sample: {sample.Id}");
                var result = await RunOneAsync(httpClient, options, template, model, sample);
                results.Add(result);
                outputBlocks.Add(new OutputBlock(model, sample, result));
            }
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var csvPath = Path.Combine(options.OutputDirectory, $"format_model_benchmark_{timestamp}.csv");
        var reportPath = Path.Combine(options.OutputDirectory, $"format_model_benchmark_{timestamp}.md");
        var outputsPath = Path.Combine(options.OutputDirectory, $"format_model_outputs_{timestamp}.md");

        File.WriteAllText(csvPath, RenderCsv(results), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(reportPath, RenderSummary(results, options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(outputsPath, RenderOutputs(outputBlocks), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine($"CSV: {csvPath}");
        Console.WriteLine($"Summary: {reportPath}");
        Console.WriteLine($"Outputs: {outputsPath}");
        return 0;
    }

    private static async Task<BenchmarkResult> RunOneAsync(
        HttpClient httpClient,
        BenchmarkOptions options,
        string template,
        string model,
        BenchmarkSample sample)
    {
        var timeoutSeconds = options.GetTimeoutSeconds(model);
        var prompt = PromptBuilder.Build(template, [], sample.Text);
        var requestJson = OllamaRequestBuilder.BuildChatRequestJson(
            model,
            prompt,
            options.Temperature,
            gpuLayers: options.GpuLayers,
            numPredict: options.NumPredict,
            numContext: options.NumContext,
            keepAlive: options.KeepAlive);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync("api/chat", content, timeoutCts.Token);
            var responseJson = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return BenchmarkResult.FromError(
                    model,
                    sample,
                    stopwatch.ElapsedMilliseconds,
                    timedOut: false,
                    $"HTTP {(int)response.StatusCode}: {TrimForCell(responseJson)}");
            }

            var parsed = OllamaRequestBuilder.ParseChatResponse(responseJson);
            var containsThinkTag = parsed?.Contains("<think", StringComparison.OrdinalIgnoreCase) == true;
            var postProcessed = FormatPostProcessor.RemoveThinkTags(parsed);
            return BenchmarkResult.FromOutput(
                model,
                sample,
                stopwatch.ElapsedMilliseconds,
                postProcessed.Text,
                containsThinkTag,
                postProcessed.ThinkTagRemoved);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return BenchmarkResult.FromError(
                model,
                sample,
                stopwatch.ElapsedMilliseconds,
                timedOut: true,
                $"timeout after {timeoutSeconds}s");
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            return BenchmarkResult.FromError(
                model,
                sample,
                stopwatch.ElapsedMilliseconds,
                timedOut: false,
                ex.Message);
        }
    }

    private static IReadOnlyList<BenchmarkSample> LoadSamples(string inputDirectory)
    {
        if (!Directory.Exists(inputDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(inputDirectory, "*.txt")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new BenchmarkSample(
                Path.GetFileNameWithoutExtension(path),
                Path.GetFileName(path),
                File.ReadAllText(path, Encoding.UTF8).Trim()))
            .ToList();
    }

    private static string RenderCsv(IReadOnlyList<BenchmarkResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "model,sample_id,sample_name,elapsed_ms,timed_out,error,output_text,output_chars,contains_preamble,contains_think_tag,think_tag_removed,changed_terms,missing_required_terms,added_unspoken_content,meaning_changed_suspected,disqualified,score_quality,score_performance,score_reliability,score_total");
        foreach (var result in results)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(result.Model),
                Csv(result.SampleId),
                Csv(result.SampleName),
                result.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                result.TimedOut.ToString(CultureInfo.InvariantCulture),
                Csv(result.Error),
                Csv(result.OutputText),
                result.OutputChars.ToString(CultureInfo.InvariantCulture),
                result.ContainsPreamble.ToString(CultureInfo.InvariantCulture),
                result.ContainsThinkTag.ToString(CultureInfo.InvariantCulture),
                result.ThinkTagRemoved.ToString(CultureInfo.InvariantCulture),
                Csv(string.Join("; ", result.ChangedTerms)),
                Csv(string.Join("; ", result.MissingRequiredTerms)),
                result.AddedUnspokenContent.ToString(CultureInfo.InvariantCulture),
                result.MeaningChangedSuspected.ToString(CultureInfo.InvariantCulture),
                result.Disqualified.ToString(CultureInfo.InvariantCulture),
                result.ScoreQuality.ToString(CultureInfo.InvariantCulture),
                result.ScorePerformance.ToString(CultureInfo.InvariantCulture),
                result.ScoreReliability.ToString(CultureInfo.InvariantCulture),
                result.ScoreTotal.ToString(CultureInfo.InvariantCulture),
            }));
        }

        return builder.ToString();
    }

    private static string RenderSummary(IReadOnlyList<BenchmarkResult> results, BenchmarkOptions options)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Local Formatter Model Benchmark");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Endpoint: `{options.Endpoint}`");
        builder.AppendLine($"Temperature: `{options.Temperature}` / num_predict: `{options.NumPredict}` / num_ctx: `{options.NumContext}` / keep_alive: `{options.KeepAlive}`");
        builder.AppendLine();
        builder.AppendLine("| Model | Runs | Avg ms | Timeout % | Empty outputs | Disqualified | Avg score | Changed terms | Think tag seen | Preamble seen |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var group in results.GroupBy(r => r.Model).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var count = group.Count();
            var avgMs = group.Average(r => r.ElapsedMilliseconds);
            var timeoutRate = group.Count(r => r.TimedOut) * 100.0 / count;
            var avgScore = group.Average(r => r.ScoreTotal);
            builder.AppendLine(
                $"| `{group.Key}` | {count} | {avgMs:F0} | {timeoutRate:F1}% | {group.Count(r => r.OutputChars == 0)} | {group.Count(r => r.Disqualified)} | {avgScore:F1} | {group.Count(r => r.ChangedTerms.Count > 0)} | {group.Count(r => r.ContainsThinkTag)} | {group.Count(r => r.ContainsPreamble)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Candidate Picks");
        builder.AppendLine();
        var bestFast = PickBest(results, ModelClass.Fast);
        var bestQuality = PickBest(results, ModelClass.Quality);
        var recommended = PickRecommended(results);
        builder.AppendLine($"- Best Fast Model: {FormatPick(bestFast)}");
        builder.AppendLine($"- Best Quality Model: {FormatPick(bestQuality)}");
        builder.AppendLine($"- Recommended Default Candidate: {FormatPick(recommended)}");
        builder.AppendLine();
        builder.AppendLine("Final adoption still requires human review of output samples. Do not change appsettings.json defaults before approval.");
        builder.AppendLine();
        builder.AppendLine("## Human Review Notes");
        builder.AppendLine();
        builder.AppendLine("- TinySwallow remains a strong fast candidate, but it is not automatically approved.");
        builder.AppendLine("- TinySwallow sample_001 changes the request wording into a descriptive sentence.");
        builder.AppendLine("- TinySwallow sample_003 leaves `同期で` in the output.");
        builder.AppendLine("- TinySwallow sample_007 does not recover the ASR misrecognition.");
        builder.AppendLine("- TinySwallow is promising as a fast candidate, but approval requires human review.");
        builder.AppendLine("- Keep gemma3:1b as a Fast candidate so reviewers can compare speed, meaning preservation, self-correction cleanup, and proper noun preservation against TinySwallow.");
        builder.AppendLine();
        builder.AppendLine("## Review-only Appsettings Proposal");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine("{");
        builder.AppendLine("  \"llm\": {");
        builder.AppendLine("    \"enabled\": false,");
        builder.AppendLine($"    \"model\": \"{bestFast?.Model ?? "<selected-fast-model>"}\",");
        builder.AppendLine($"    \"qualityModel\": \"{bestQuality?.Model ?? "<selected-quality-model>"}\",");
        builder.AppendLine("    \"timeoutSeconds\": 12,");
        builder.AppendLine("    \"qualityTimeoutSeconds\": 60,");
        builder.AppendLine("    \"fallbackToRaw\": true,");
        builder.AppendLine("    \"rawFirstPaste\": true,");
        builder.AppendLine("    \"gpuLayers\": 0");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string RenderOutputs(IReadOnlyList<OutputBlock> blocks)
    {
        var builder = new StringBuilder();
        foreach (var modelGroup in blocks.GroupBy(b => b.Model))
        {
            builder.AppendLine($"## model: {modelGroup.Key}");
            builder.AppendLine();
            foreach (var block in modelGroup)
            {
                builder.AppendLine($"### {block.Sample.Id}");
                builder.AppendLine("Input:");
                builder.AppendLine("```text");
                builder.AppendLine(block.Sample.Text);
                builder.AppendLine("```");
                builder.AppendLine("Output:");
                builder.AppendLine("```text");
                builder.AppendLine(block.Result.OutputText ?? "");
                builder.AppendLine("```");
                builder.AppendLine("Auto notes:");
                builder.AppendLine($"- elapsed_ms: {block.Result.ElapsedMilliseconds}");
                builder.AppendLine($"- timed_out: {block.Result.TimedOut}");
                builder.AppendLine($"- error: {block.Result.Error}");
                builder.AppendLine($"- changed_terms: {string.Join(", ", block.Result.ChangedTerms)}");
                builder.AppendLine($"- missing_required_terms: {string.Join(", ", block.Result.MissingRequiredTerms)}");
                builder.AppendLine($"- disqualified: {block.Result.Disqualified}");
                builder.AppendLine($"- contains_preamble: {block.Result.ContainsPreamble}");
                builder.AppendLine($"- contains_think_tag: {block.Result.ContainsThinkTag}");
                builder.AppendLine($"- think_tag_removed: {block.Result.ThinkTagRemoved}");
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static ModelPick? PickBest(IReadOnlyList<BenchmarkResult> results, ModelClass modelClass)
    {
        return results
            .Where(r => ClassifyModel(r.Model) == modelClass)
            .GroupBy(r => r.Model)
            .Select(ToModelAggregate)
            .Where(IsQualifyingCandidate)
            .OrderByDescending(x => x.AvgScore)
            .ThenBy(x => x.AvgMs)
            .Select(x => new ModelPick(x.Model, x.AvgScore, x.AvgMs))
            .FirstOrDefault();
    }

    private static ModelPick? PickRecommended(IReadOnlyList<BenchmarkResult> results)
    {
        return results
            .GroupBy(r => r.Model)
            .Select(ToModelAggregate)
            .Where(IsQualifyingCandidate)
            .OrderByDescending(x => x.AvgScore)
            .ThenBy(x => x.AvgMs)
            .Select(x => new ModelPick(x.Model, x.AvgScore, x.AvgMs))
            .FirstOrDefault();
    }

    private static ModelAggregate ToModelAggregate(
        IGrouping<string, BenchmarkResult> group)
        => new(
            group.Key,
            group.Average(r => r.ScoreTotal),
            group.Count(r => r.TimedOut) * 100.0 / group.Count(),
            group.Average(r => r.ElapsedMilliseconds),
            group.Count(r => r.OutputChars == 0),
            group.Count(r => r.Disqualified));

    private static bool IsQualifyingCandidate(
        ModelAggregate candidate)
        => candidate.TimeoutRate <= 10 &&
           candidate.EmptyOutputs == 0 &&
           candidate.DisqualifiedRuns == 0 &&
           candidate.AvgScore > 0;

    private static string FormatPick(ModelPick? pick)
    {
        return pick is null
            ? "No qualifying candidate"
            : $"{pick.Model} ({pick.AvgScore:F1}, {pick.AvgMilliseconds:F0}ms avg)";
    }

    private static ModelClass ClassifyModel(string model)
    {
        if (model.StartsWith("gemma4:", StringComparison.OrdinalIgnoreCase))
        {
            return ModelClass.Reference;
        }

        if (model.Contains("0.6b", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("1b", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("1.5b", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("1.7b", StringComparison.OrdinalIgnoreCase))
        {
            return ModelClass.Fast;
        }

        return ModelClass.Quality;
    }

    private static string Csv(string? value)
        => "\"" + (value ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string TrimForCell(string value)
        => value.Length <= 300 ? value : value[..300];

    private enum ModelClass
    {
        Fast,
        Quality,
        Reference,
    }

    private sealed record ModelPick(string Model, double AvgScore, double AvgMilliseconds);

    private sealed record ModelAggregate(
        string Model,
        double AvgScore,
        double TimeoutRate,
        double AvgMs,
        int EmptyOutputs,
        int DisqualifiedRuns);

    private sealed record BenchmarkOptions(
        IReadOnlyList<string> Models,
        string InputDirectory,
        string OutputDirectory,
        string PromptPath,
        string Endpoint,
        double Temperature,
        int? GpuLayers,
        int? NumPredict,
        int? NumContext,
        string? KeepAlive,
        int TimeoutFast,
        int TimeoutQuality,
        int TimeoutReference)
    {
        public static BenchmarkOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var key = args[i][2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    values[key] = args[++i];
                }
            }

            var models = Get(values, "models", DefaultModels)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new BenchmarkOptions(
                models,
                Get(values, "input", Path.Combine("samples", "format_benchmark", "input")),
                Get(values, "output", "reports"),
                Get(values, "prompt", Path.Combine("prompts", "plain.md")),
                Get(values, "endpoint", "http://localhost:11434"),
                GetDouble(values, "temperature", 0.1),
                GetNullableInt(values, "gpu-layers"),
                GetNullableInt(values, "num-predict") ?? 128,
                GetNullableInt(values, "num-ctx") ?? 1024,
                Get(values, "keep-alive", "30m"),
                GetInt(values, "timeout-fast", 30),
                GetInt(values, "timeout-quality", 120),
                GetInt(values, "timeout-reference", 180));
        }

        public int GetTimeoutSeconds(string model)
            => ClassifyModel(model) switch
            {
                ModelClass.Fast => TimeoutFast,
                ModelClass.Quality => TimeoutQuality,
                _ => TimeoutReference,
            };

        private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback)
            => values.TryGetValue(key, out var value) ? value : fallback;

        private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
            => values.TryGetValue(key, out var value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : fallback;

        private static int? GetNullableInt(IReadOnlyDictionary<string, string> values, string key)
            => values.TryGetValue(key, out var value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;

        private static double GetDouble(IReadOnlyDictionary<string, string> values, string key, double fallback)
            => values.TryGetValue(key, out var value) &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : fallback;
    }

    private sealed record BenchmarkSample(string Id, string Name, string Text);

    private sealed record OutputBlock(string Model, BenchmarkSample Sample, BenchmarkResult Result);

    private sealed record BenchmarkResult(
        string Model,
        string SampleId,
        string SampleName,
        long ElapsedMilliseconds,
        bool TimedOut,
        string? Error,
        string? OutputText,
        int OutputChars,
        bool ContainsPreamble,
        bool ContainsThinkTag,
        bool ThinkTagRemoved,
        IReadOnlyList<string> ChangedTerms,
        IReadOnlyList<string> MissingRequiredTerms,
        bool AddedUnspokenContent,
        bool MeaningChangedSuspected,
        bool Disqualified,
        int ScoreQuality,
        int ScorePerformance,
        int ScoreReliability,
        int ScoreTotal)
    {
        public static BenchmarkResult FromError(
            string model,
            BenchmarkSample sample,
            long elapsedMilliseconds,
            bool timedOut,
            string error)
            => new(
                model,
                sample.Id,
                sample.Name,
                elapsedMilliseconds,
                timedOut,
                error,
                null,
                0,
                false,
                false,
                false,
                [],
                RequiredTerms.Where(term => sample.Text.Contains(term, StringComparison.Ordinal)).ToList(),
                false,
                false,
                true,
                0,
                0,
                0,
                0);

        public static BenchmarkResult FromOutput(
            string model,
            BenchmarkSample sample,
            long elapsedMilliseconds,
            string? outputText,
            bool containsThinkTag,
            bool thinkTagRemoved)
        {
            var output = outputText ?? "";
            var isEmptyOutput = string.IsNullOrWhiteSpace(output);
            var changedTerms = RequiredTerms
                .Where(term => sample.Text.Contains(term, StringComparison.Ordinal) &&
                               !output.Contains(term, StringComparison.Ordinal))
                .ToList();
            var missingTerms = changedTerms.ToList();
            var containsPreamble = ContainsPreambleText(output);
            var addedUnspokenContent = output.Length > Math.Max(sample.Text.Length * 3, sample.Text.Length + 180);
            var tooManyMissingRequiredTerms = missingTerms.Count >= RequiredTermDisqualificationThreshold;
            var meaningChangedSuspected = output.Length < sample.Text.Length / 4 || changedTerms.Count > 0;
            var disqualified = isEmptyOutput || tooManyMissingRequiredTerms;

            if (disqualified)
            {
                return new BenchmarkResult(
                    model,
                    sample.Id,
                    sample.Name,
                    elapsedMilliseconds,
                    TimedOut: false,
                    Error: isEmptyOutput
                        ? "empty output"
                        : $"missing required terms >= {RequiredTermDisqualificationThreshold}",
                    output,
                    output.Length,
                    containsPreamble,
                    containsThinkTag,
                    thinkTagRemoved,
                    changedTerms,
                    missingTerms,
                    addedUnspokenContent,
                    meaningChangedSuspected,
                    true,
                    0,
                    0,
                    0,
                    0);
            }

            var quality = 60;
            quality -= changedTerms.Count * 15;
            quality -= containsPreamble ? 8 : 0;
            quality -= addedUnspokenContent ? 8 : 0;
            quality -= meaningChangedSuspected ? 8 : 0;
            quality = Math.Clamp(quality, 0, 60);
            var performance = elapsedMilliseconds <= 15_000 ? 25 :
                elapsedMilliseconds <= 60_000 ? 18 :
                elapsedMilliseconds <= 120_000 ? 10 : 4;
            var reliability = 10;
            reliability -= string.IsNullOrWhiteSpace(output) ? 5 : 0;
            reliability -= containsThinkTag && !thinkTagRemoved ? 3 : 0;
            reliability -= containsPreamble ? 2 : 0;
            reliability = Math.Clamp(reliability, 0, 10);
            var operational = 5;

            return new BenchmarkResult(
                model,
                sample.Id,
                sample.Name,
                elapsedMilliseconds,
                TimedOut: false,
                Error: null,
                output,
                output.Length,
                containsPreamble,
                containsThinkTag,
                thinkTagRemoved,
                changedTerms,
                missingTerms,
                addedUnspokenContent,
                meaningChangedSuspected,
                false,
                quality,
                performance,
                reliability,
                quality + performance + reliability + operational);
        }

        private static bool ContainsPreambleText(string output)
            => output.Contains("以下", StringComparison.Ordinal) ||
               output.Contains("整形されたテキスト", StringComparison.Ordinal) ||
               output.Contains("整形結果", StringComparison.Ordinal) ||
               output.Contains("Here", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Output", StringComparison.OrdinalIgnoreCase);
    }
}
