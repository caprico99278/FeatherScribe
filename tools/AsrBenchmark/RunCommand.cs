using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;
using NAudio.Wave;

namespace FeatherScribe.Tools.AsrBenchmark;

/// <summary>
/// Transcribes every corpus wav with every model using the production whisper-cli arguments
/// (WhisperCppCommandBuilder.BuildArguments, language ja) and writes results.json + report.md.
/// </summary>
internal static class RunCommand
{
    private const string Language = "ja";

    public static async Task<int> RunAsync(CommandLine commandLine)
    {
        var corpusPath = commandLine.GetPath("corpus", "benchmark/asr/corpus.json");
        var audioDirectory = commandLine.GetPath("audio", "local/benchmark/audio");
        var whisperPath = commandLine.GetPath("whisper", "local/whisper/Release/whisper-cli.exe");
        var threads = Math.Max(1, commandLine.GetInt("threads", 4));
        var timeoutSeconds = Math.Max(1, commandLine.GetInt("timeout", 300));
        var outDirectory = commandLine.GetPath(
            "out",
            Path.Combine("reports", "asr-benchmark", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)));
        var models = commandLine.Require("models")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFullPath)
            .ToList();

        var corpus = AsrCorpus.Load(corpusPath);
        if (!File.Exists(whisperPath))
        {
            throw new FileNotFoundException($"whisper-cli not found: {whisperPath}");
        }

        if (!Directory.Exists(audioDirectory))
        {
            throw new DirectoryNotFoundException($"Audio folder not found: {audioDirectory}");
        }

        if (models.Count == 0)
        {
            throw new ArgumentException("--models needs at least one model path.");
        }

        if (models.FirstOrDefault(m => !File.Exists(m)) is { } missingModel)
        {
            throw new FileNotFoundException($"Model not found: {missingModel}");
        }

        var audioSet = AudioSetMarker.Read(audioDirectory);
        Console.WriteLine(BenchmarkReport.AudioKindBanner(audioSet.Kind));
        var withAudio = corpus.Count(item => File.Exists(Path.Combine(audioDirectory, item.Id + ".wav")));
        if (withAudio < corpus.Count)
        {
            Console.WriteLine($"Partial audio set: {withAudio} of {corpus.Count} utterances have audio; the rest are skipped.");
        }
        Directory.CreateDirectory(outDirectory);
        var workDirectory = Path.Combine(Path.GetTempPath(), "FeatherScribe", "asr-benchmark", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        var rows = new List<UtteranceResult>();
        try
        {
            foreach (var model in models)
            {
                var modelName = Path.GetFileNameWithoutExtension(model);
                var settings = new AsrSettings
                {
                    WhisperExecutablePath = whisperPath,
                    ModelPath = model,
                    Language = Language,
                    Threads = threads,
                    TimeoutSeconds = timeoutSeconds,
                };

                // One untimed warm-up run per model (file cache / first-load effects).
                var warmupAudio = corpus
                    .Select(item => Path.Combine(audioDirectory, item.Id + ".wav"))
                    .FirstOrDefault(File.Exists);
                if (warmupAudio is not null)
                {
                    Console.WriteLine($"[{modelName}] warm-up ...");
                    var warmup = await TranscribeAsync(settings, warmupAudio, workDirectory).ConfigureAwait(false);
                    if (!warmup.Success)
                    {
                        Console.WriteLine($"[{modelName}] warm-up failed: {warmup.Error}");
                    }
                }

                foreach (var item in corpus)
                {
                    var wavPath = Path.Combine(audioDirectory, item.Id + ".wav");
                    var row = File.Exists(wavPath)
                        ? await MeasureAsync(settings, modelName, item, wavPath, workDirectory).ConfigureAwait(false)
                        : AudioMissingRow(modelName, item);
                    rows.Add(row);
                    if (row.AudioMissing)
                    {
                        Console.WriteLine($"[{modelName}] {item.Id} skip (audio missing)");
                        continue;
                    }

                    Console.WriteLine(
                        $"[{modelName}] {item.Id} {(row.Success ? "ok  " : "FAIL")} " +
                        $"CER {AsrMetrics.Format(row.Cer)}  {row.LatencyMs:0} ms  RTF {AsrMetrics.Format(row.RealTimeFactor, "0.00")}  " +
                        $"peak {row.PeakWorkingSetBytes / 1024 / 1024} MB");
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        var aggregates = rows
            .GroupBy(r => r.Model, StringComparer.Ordinal)
            .Select(g => AsrMetrics.Aggregate(g.Key, g.ToList()))
            .ToList();
        var metadata = new RunMetadata(
            CreatedAt: DateTimeOffset.Now,
            AudioKind: audioSet.Kind,
            AudioSource: audioSet.Source,
            CorpusUtterances: corpus.Count,
            WhisperExecutable: whisperPath,
            Language: Language,
            Threads: threads,
            TimeoutSeconds: timeoutSeconds,
            Models: models,
            LogicalProcessors: Environment.ProcessorCount,
            OsVersion: Environment.OSVersion.VersionString);

        var resultsJson = JsonSerializer.Serialize(
            new { metadata, aggregates, ranking = AsrMetrics.RankForDecision(aggregates).Select(a => a.Model), rows },
            AudioSetMarker.JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "results.json"), resultsJson.ReplaceLineEndings("\n") + "\n").ConfigureAwait(false);
        var report = BenchmarkReport.BuildMarkdown(metadata, aggregates, rows);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "report.md"), report).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(report);
        Console.WriteLine($"Output: {outDirectory}");
        return 0;
    }

    private static async Task<UtteranceResult> MeasureAsync(
        AsrSettings settings, string modelName, CorpusItem item, string wavPath, string workDirectory)
    {
        var audioSeconds = AudioSeconds(wavPath);
        var run = await TranscribeAsync(settings, wavPath, workDirectory).ConfigureAwait(false);
        var transcript = run.Success ? run.Transcript : "";
        var (cer, focus) = AsrMetrics.Score(item, transcript);

        return new UtteranceResult(
            Model: modelName,
            Id: item.Id,
            Category: item.Category,
            Reference: item.Text,
            Transcript: transcript,
            Success: run.Success,
            ExitCode: run.ExitCode,
            Error: run.Error,
            LatencyMs: run.Elapsed.TotalMilliseconds,
            AudioSeconds: audioSeconds,
            PeakWorkingSetBytes: run.PeakWorkingSetBytes,
            Cer: cer,
            NumericHits: focus.NumericHits,
            NumericTotal: focus.NumericTotal,
            ProperNounHits: focus.ProperNounHits,
            ProperNounTotal: focus.ProperNounTotal,
            MissedFocusTokens: focus.MissedTokens);
    }

    /// <summary>Row for an utterance without a wav: kept in results.json, excluded from every aggregate.</summary>
    private static UtteranceResult AudioMissingRow(string modelName, CorpusItem item)
        => new(
            modelName, item.Id, item.Category, item.Text, "", Success: false, ExitCode: null, Error: "audio missing",
            LatencyMs: 0, AudioSeconds: 0, PeakWorkingSetBytes: 0, Cer: null,
            NumericHits: 0, NumericTotal: 0, ProperNounHits: 0, ProperNounTotal: 0, MissedFocusTokens: [],
            AudioMissing: true);

    private static double AudioSeconds(string wavPath)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            return reader.TotalTime.TotalSeconds;
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidDataException)
        {
            return 0;
        }
    }

    private sealed record WhisperRun(
        bool Success, int? ExitCode, string? Error, string Transcript, TimeSpan Elapsed, long PeakWorkingSetBytes);

    /// <summary>
    /// Runs whisper-cli like WhisperCppTranscriptionEngine does (same arguments, same txt output),
    /// additionally sampling the process peak working set until it exits.
    /// </summary>
    private static async Task<WhisperRun> TranscribeAsync(AsrSettings settings, string wavPath, string workDirectory)
    {
        var outputBase = Path.Combine(workDirectory, Path.GetFileNameWithoutExtension(wavPath) + "_asr");
        var outputTxt = outputBase + ".txt";
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.WhisperExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in WhisperCppCommandBuilder.BuildArguments(settings, wavPath, outputBase))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stopwatch = Stopwatch.StartNew();
        long peak = 0;
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("whisper-cli process could not be started");
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var deadline = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            var exited = process.WaitForExitAsync();

            while (!process.HasExited)
            {
                peak = Math.Max(peak, SamplePeakWorkingSet(process));
                if (stopwatch.Elapsed > deadline)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    await process.WaitForExitAsync().ConfigureAwait(false);
                    stopwatch.Stop();
                    return new WhisperRun(false, null, $"timeout ({settings.TimeoutSeconds}s)", "", stopwatch.Elapsed, peak);
                }

                await Task.WhenAny(exited, Task.Delay(25)).ConfigureAwait(false);
            }

            await exited.ConfigureAwait(false);
            stopwatch.Stop();
            var stderr = await stderrTask.ConfigureAwait(false);
            await stdoutTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var tail = stderr.Length > 2000 ? stderr[^2000..] : stderr;
                return new WhisperRun(false, process.ExitCode, $"exit code {process.ExitCode}: {tail.Trim()}", "", stopwatch.Elapsed, peak);
            }

            if (!File.Exists(outputTxt))
            {
                return new WhisperRun(false, process.ExitCode, "whisper-cli produced no txt output", "", stopwatch.Elapsed, peak);
            }

            var transcript = (await File.ReadAllTextAsync(outputTxt, Encoding.UTF8).ConfigureAwait(false)).Trim();
            return new WhisperRun(true, process.ExitCode, null, transcript, stopwatch.Elapsed, peak);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            stopwatch.Stop();
            return new WhisperRun(false, null, ex.Message, "", stopwatch.Elapsed, peak);
        }
        finally
        {
            try
            {
                File.Delete(outputTxt);
            }
            catch (IOException)
            {
            }
        }
    }

    private static long SamplePeakWorkingSet(Process process)
    {
        try
        {
            process.Refresh();
            return process.PeakWorkingSet64;
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and the sample.
            return 0;
        }
    }
}
