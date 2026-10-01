using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tools.AsrBenchmark;

/// <summary>
/// Records the user's own voice for each corpus utterance with the production recorder
/// (NAudioRecorder, 16 kHz mono 16-bit). Output stays in a local, gitignored folder.
/// </summary>
internal static class RecordCommand
{
    private const int MaxSecondsPerUtterance = 120;

    public static async Task<int> RunAsync(CommandLine commandLine)
    {
        var corpusPath = commandLine.GetPath("corpus", "benchmark/asr/corpus.json");
        var outDirectory = commandLine.GetPath("out", "local/benchmark/audio");
        var corpus = AsrCorpus.Load(corpusPath);

        var only = commandLine.Get("only")?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = corpus.Where(item => only is null || only.Contains(item.Id)).ToList();
        if (only is not null)
        {
            var unknown = only.Where(id => corpus.All(item => !item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToList();
            if (unknown.Count > 0)
            {
                Console.Error.WriteLine("Unknown utterance id(s): " + string.Join(", ", unknown));
                return 2;
            }
        }

        Directory.CreateDirectory(outDirectory);

        // Mark the folder as real before the first utterance, so an interrupted or split
        // recording session still leaves a correctly marked audio set.
        var conflict = AudioSetMarker.PrepareForRecording(
            outDirectory, AudioSetKind.Real, AudioSetMarker.RealRecordingSource, DateTimeOffset.Now);
        if (conflict is not null)
        {
            Console.Error.WriteLine(conflict);
            return 2;
        }

        var tempDirectory = Path.Combine(outDirectory, ".recording-tmp");
        var recorder = new NAudioRecorder(
            new RecordingSettings { SampleRate = 16000, Channels = 1, MaxRecordingSeconds = MaxSecondsPerUtterance },
            tempDirectory);

        Console.WriteLine($"Recording {targets.Count} utterance(s) to {outDirectory}");
        Console.WriteLine("Read each text exactly as written, in your normal dictation voice.");
        Console.WriteLine("The audio stays on this PC only (gitignored). Delete the folder to remove it.");

        for (var index = 0; index < targets.Count; index++)
        {
            var item = targets[index];
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine($"[{index + 1}/{targets.Count}] {item.Id} ({item.Category})" +
                    (item.ReadingNote is null ? "" : $"  * {item.ReadingNote}"));
                Console.WriteLine($"  {item.Text}");
                Console.Write("  Enter = start recording ... ");
                Console.ReadLine();

                using var stop = new CancellationTokenSource();
                var recording = recorder.RecordUntilStoppedAsync(stop.Token);
                Console.Write($"  REC (max {MaxSecondsPerUtterance}s)  Enter = stop ... ");
                Console.ReadLine();
                stop.Cancel();
                var recorded = await recording.ConfigureAwait(false);

                var wavPath = Path.Combine(outDirectory, item.Id + ".wav");
                File.Move(recorded.File.Path, wavPath, overwrite: true);
                Console.WriteLine($"  saved {item.Id}.wav ({recorded.File.Duration.TotalSeconds:0.0}s)");
                if (AudioSetMarker.TryRefresh(
                        outDirectory, AudioSetKind.Real, AudioSetMarker.RealRecordingSource, DateTimeOffset.Now) is { } refreshError)
                {
                    Console.WriteLine($"  warning: could not refresh {AudioSetMarker.FileName}: {refreshError}");
                }

                Console.Write("  Enter = next, r + Enter = record again ... ");
                var answer = Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "r", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        TryDeleteEmptyDirectory(tempDirectory);
        Console.WriteLine();
        Console.WriteLine($"Done. Audio set marked real: {outDirectory}");
        Console.WriteLine("Recording can be split across sessions: record --only <ids> adds or replaces single utterances.");
        return 0;
    }

    private static void TryDeleteEmptyDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (IOException)
        {
        }
    }
}
