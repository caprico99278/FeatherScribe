using System.IO;
using NAudio.Wave;
using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// 既定マイクから 16bit PCM WAV を一時ファイルへ録音する。
/// キャンセルトークンのキャンセルが「録音停止」の合図。
/// </summary>
public sealed class NAudioRecorder : IAudioRecorder
{
    private readonly RecordingSettings _settings;
    private readonly string _tempDirectory;

    public NAudioRecorder(RecordingSettings settings, string? tempDirectory = null)
    {
        _settings = settings;
        _tempDirectory = tempDirectory
            ?? Path.Combine(Path.GetTempPath(), "FeatherScribe", "recordings");
    }

    public async Task<RecordedAudio> RecordUntilStoppedAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_tempDirectory);
        var startedAt = DateTimeOffset.Now;
        var wavPath = Path.Combine(_tempDirectory, $"rec_{startedAt:yyyyMMdd_HHmmss_fff}.wav");

        var stopped = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(_settings.SampleRate, 16, Math.Max(1, _settings.Channels)),
            BufferMilliseconds = 50,
        };

        var writer = new WaveFileWriter(wavPath, waveIn.WaveFormat);
        try
        {
            waveIn.DataAvailable += (_, e) =>
            {
                lock (writer)
                {
                    writer.Write(e.Buffer, 0, e.BytesRecorded);
                }
            };
            waveIn.RecordingStopped += (_, e) => stopped.TrySetResult(e.Exception);

            waveIn.StartRecording();

            // 停止合図: トークンのキャンセル、または最大録音時間の超過
            using var maxDurationCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(Math.Max(1, _settings.MaxRecordingSeconds)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, maxDurationCts.Token);
            await using var registration = linked.Token.Register(() => waveIn.StopRecording());

            var deviceError = await stopped.Task.ConfigureAwait(false);
            if (deviceError is not null)
            {
                throw new InvalidOperationException(
                    $"録音デバイスでエラーが発生しました: {deviceError.Message}", deviceError);
            }
        }
        finally
        {
            TimeSpan duration;
            lock (writer)
            {
                duration = writer.TotalTime;
                writer.Dispose();
            }
            LastDuration = duration;
        }

        return new RecordedAudio(
            new AudioFile(wavPath, LastDuration),
            startedAt,
            DateTimeOffset.Now);
    }

    private TimeSpan LastDuration { get; set; }
}
