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

    /// <summary>
    /// Scalar input level (0..1) of the latest recording buffer, for the recording meter only.
    /// Raised on the audio callback thread (about every 50 ms); 0 is raised once after recording stops.
    /// Never carries samples. Not part of <see cref="IAudioRecorder"/>; the pipeline never sees it.
    /// </summary>
    public event Action<float>? AudioLevelChanged;

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
        var stoppedAtMaxDuration = false;
        try
        {
            waveIn.DataAvailable += (_, e) =>
            {
                lock (writer)
                {
                    writer.Write(e.Buffer, 0, e.BytesRecorded);
                }

                // Outside the writer lock: the level is UI feedback only.
                RaiseAudioLevel(e.Buffer, e.BytesRecorded);
            };
            waveIn.RecordingStopped += (_, e) => stopped.TrySetResult(e.Exception);

            waveIn.StartRecording();

            // 停止合図: トークンのキャンセル、または最大録音時間の超過
            using var maxDurationCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(Math.Max(1, _settings.MaxRecordingSeconds)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, maxDurationCts.Token);
            // The linked callback runs once, for whichever signal came first: only a limit stop is reported.
            await using var registration = linked.Token.Register(() =>
            {
                stoppedAtMaxDuration = IsMaxDurationStop(
                    userStopRequested: cancellationToken.IsCancellationRequested,
                    maxDurationElapsed: maxDurationCts.IsCancellationRequested);
                waveIn.StopRecording();
            });

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
            RaiseAudioLevelStopped();
        }

        return new RecordedAudio(
            new AudioFile(wavPath, LastDuration),
            startedAt,
            DateTimeOffset.Now,
            stoppedAtMaxDuration);
    }

    /// <summary>
    /// Stop reason when the first stop signal fires: the recording limit counts only when the user had not
    /// already asked to stop (a hotkey press that wins the race is a normal stop, with no notice).
    /// </summary>
    internal static bool IsMaxDurationStop(bool userStopRequested, bool maxDurationElapsed)
        => maxDurationElapsed && !userStopRequested;

    private TimeSpan LastDuration { get; set; }

    private void RaiseAudioLevel(byte[] buffer, int bytesRecorded)
    {
        try
        {
            var handler = AudioLevelChanged;
            if (handler is not null)
            {
                handler(AudioLevelMeter.ComputeLevel(buffer, bytesRecorded));
            }
        }
        catch (Exception)
        {
            // The level UI must never stop recording; the meter just stays still.
        }
    }

    private void RaiseAudioLevelStopped()
    {
        try
        {
            AudioLevelChanged?.Invoke(0f);
        }
        catch (Exception)
        {
            // The level UI must never break the end of recording.
        }
    }
}
