// FeatherScribe ASR benchmark (Phase QUALITY-1).
// Compares whisper.cpp models on a fixed corpus with the production whisper-cli arguments.
//   record     : record the user's own voice for each corpus utterance (local/benchmark/audio)
//   synthesize : TTS wavs for tooling validation only (local/benchmark/tts, marked synthetic)
//   run        : transcribe with each model, write results.json + report.md under reports/asr-benchmark/
using System.Text;
using FeatherScribe.Tools.AsrBenchmark;

Console.OutputEncoding = Encoding.UTF8;

CommandLine commandLine;
try
{
    commandLine = CommandLine.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    PrintUsage();
    return 2;
}

try
{
    return commandLine.Command switch
    {
        "record" => await RecordCommand.RunAsync(commandLine),
        "synthesize" => SynthesizeCommand.Run(commandLine),
        "run" => await RunCommand.RunAsync(commandLine),
        _ => Unknown(commandLine.Command),
    };
}
catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or DirectoryNotFoundException or InvalidDataException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage (run from the repository root):
          dotnet run --project tools/AsrBenchmark -- record     [--corpus benchmark/asr/corpus.json] [--out local/benchmark/audio] [--only u05,u12]
          dotnet run --project tools/AsrBenchmark -- synthesize [--corpus benchmark/asr/corpus.json] [--out local/benchmark/tts]
          dotnet run --project tools/AsrBenchmark -- run --models <model1.bin;model2.bin> [--audio local/benchmark/audio]
                     [--corpus benchmark/asr/corpus.json] [--whisper local/whisper/Release/whisper-cli.exe]
                     [--threads 4] [--timeout 300] [--out reports/asr-benchmark/<yyyyMMdd_HHmmss>]
        """);
}
