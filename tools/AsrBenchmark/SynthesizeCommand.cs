using System.Speech.AudioFormat;
using System.Speech.Synthesis;

namespace FeatherScribe.Tools.AsrBenchmark;

/// <summary>
/// TTS wavs (16 kHz mono 16-bit) for validating the tooling only. The folder is marked
/// synthetic so a run on it can never be mistaken for a real-voice benchmark.
/// </summary>
internal static class SynthesizeCommand
{
    // Utterances whose reading note asks for fast speech are spoken faster (System.Speech rate -10..10).
    private const int FastRate = 4;

    public static int Run(CommandLine commandLine)
    {
        var corpusPath = commandLine.GetPath("corpus", "benchmark/asr/corpus.json");
        var outDirectory = commandLine.GetPath("out", "local/benchmark/tts");
        var corpus = AsrCorpus.Load(corpusPath);

        using var synthesizer = new SpeechSynthesizer();
        var japaneseVoice = synthesizer.GetInstalledVoices()
            .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
        if (japaneseVoice is null)
        {
            Console.Error.WriteLine("No Japanese System.Speech voice is installed.");
            Console.Error.WriteLine("Installed voices: " + string.Join(", ",
                synthesizer.GetInstalledVoices().Select(v => $"{v.VoiceInfo.Name} ({v.VoiceInfo.Culture.Name})")));
            return 1;
        }

        Directory.CreateDirectory(outDirectory);

        // Never mix TTS wavs into a folder of real recordings; mark the folder synthetic up front.
        var conflict = AudioSetMarker.PrepareForRecording(
            outDirectory, AudioSetKind.Synthetic, $"System.Speech TTS: {japaneseVoice.VoiceInfo.Name}", DateTimeOffset.Now);
        if (conflict is not null)
        {
            Console.Error.WriteLine(conflict);
            return 2;
        }

        synthesizer.SelectVoice(japaneseVoice.VoiceInfo.Name);
        var format = new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono);

        foreach (var item in corpus)
        {
            var wavPath = Path.Combine(outDirectory, item.Id + ".wav");
            synthesizer.Rate = IsFast(item) ? FastRate : 0;
            synthesizer.SetOutputToWaveFile(wavPath, format);
            synthesizer.Speak(item.Text);
            synthesizer.SetOutputToNull();
            Console.WriteLine($"{item.Id} -> {wavPath}");
        }

        Console.WriteLine($"Voice : {japaneseVoice.VoiceInfo.Name}");
        Console.WriteLine($"Output: {outDirectory} ({corpus.Count} wavs, marked synthetic)");
        Console.WriteLine("Synthetic audio validates the tooling only; never lock the model from it.");
        return 0;
    }

    private static bool IsFast(CorpusItem item)
        => item.Category == "fast" || (item.ReadingNote?.Contains("早口", StringComparison.Ordinal) ?? false);
}
