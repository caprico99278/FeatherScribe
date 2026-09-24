using System.IO;
using System.Diagnostics;
using System.Text;
using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// whisper.cpp の whisper-cli.exe を外部プロセスとして呼び出す文字起こしエンジン。
/// 失敗時は exit code と stderr を ErrorMessage に含める(指示書§12.1)。
/// </summary>
public sealed class WhisperCppTranscriptionEngine : ISpeechToTextEngine
{
    private readonly AsrSettings _settings;

    public WhisperCppTranscriptionEngine(AsrSettings settings)
    {
        _settings = settings;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        AudioFile audioFile,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!File.Exists(_settings.WhisperExecutablePath))
        {
            return Failure(stopwatch, $"whisper-cli が見つかりません: {_settings.WhisperExecutablePath}");
        }

        if (!File.Exists(_settings.ModelPath))
        {
            return Failure(stopwatch, $"モデルファイルが見つかりません: {_settings.ModelPath}");
        }

        if (!File.Exists(audioFile.Path))
        {
            return Failure(stopwatch, $"音声ファイルが見つかりません: {audioFile.Path}");
        }

        var outputBase = Path.Combine(
            Path.GetDirectoryName(audioFile.Path) ?? Path.GetTempPath(),
            Path.GetFileNameWithoutExtension(audioFile.Path) + "_asr");
        var outputTxt = outputBase + ".txt";

        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.WhisperExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in WhisperCppCommandBuilder.BuildArguments(_settings, audioFile.Path, outputBase))
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Failure(stopwatch, "whisper-cli プロセスを起動できませんでした");
            }

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

                return cancellationToken.IsCancellationRequested
                    ? Failure(stopwatch, "キャンセルされました")
                    : Failure(stopwatch, $"whisper-cli がタイムアウトしました ({_settings.TimeoutSeconds}秒)");
            }

            var stderr = await stderrTask.ConfigureAwait(false);
            await stdoutTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var stderrTail = stderr.Length > 2000 ? stderr[^2000..] : stderr;
                return Failure(stopwatch, $"whisper-cli exit code {process.ExitCode}: {stderrTail.Trim()}");
            }

            if (!File.Exists(outputTxt))
            {
                return Failure(stopwatch, $"whisper-cli の出力ファイルがありません: {outputTxt}");
            }

            var rawText = (await File.ReadAllTextAsync(outputTxt, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false)).Trim();

            stopwatch.Stop();
            return new TranscriptionResult(rawText, stopwatch.Elapsed, IsSuccess: true, ErrorMessage: null);
        }
        finally
        {
            // 生文字起こしファイルをデフォルトで残さない(指示書§11.1)
            try
            {
                if (File.Exists(outputTxt))
                {
                    File.Delete(outputTxt);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static TranscriptionResult Failure(Stopwatch stopwatch, string message)
    {
        stopwatch.Stop();
        return new TranscriptionResult("", stopwatch.Elapsed, IsSuccess: false, ErrorMessage: message);
    }
}
