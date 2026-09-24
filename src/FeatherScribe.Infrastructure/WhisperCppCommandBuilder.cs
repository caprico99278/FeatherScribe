using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// whisper-cli.exe のコマンドライン引数を組み立てる。
/// </summary>
public static class WhisperCppCommandBuilder
{
    /// <param name="outputBasePath">拡張子なしの出力ベースパス。whisper-cli が {base}.txt を生成する。</param>
    public static IReadOnlyList<string> BuildArguments(
        AsrSettings asr,
        string audioFilePath,
        string outputBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputBasePath);

        return
        [
            "-m", asr.ModelPath,
            "-l", string.IsNullOrWhiteSpace(asr.Language) ? "ja" : asr.Language,
            "-t", Math.Max(1, asr.Threads).ToString(),
            "--output-txt",
            "--output-file", outputBasePath,
            "--no-prints",
            "-f", audioFilePath,
        ];
    }
}
