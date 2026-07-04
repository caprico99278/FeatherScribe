using FeatherScribe.Core;

namespace FeatherScribe.App;

/// <summary>
/// ホットキーによる録音トグルとパイプライン実行の状態管理。
/// Idle → (ホットキー) → Recording → (再押下) → Processing → Idle
/// </summary>
public sealed class DictationController
{
    private enum State
    {
        Idle,
        Recording,
        Processing,
    }

    private readonly DictationPipeline _pipeline;
    private readonly AppSettings _settings;
    private readonly object _gate = new();

    private State _state = State.Idle;
    private CancellationTokenSource? _stopRecordingCts;

    public event Action<PipelineResult>? Completed;

    /// <summary>バックグラウンド整形の完了通知 (成功/失敗)。</summary>
    public event Action<BackgroundFormattingResult>? BackgroundFormattingCompleted;

    /// <summary>直近結果 (再コピー/再貼り付け用)。バックグラウンド整形成功時は整形結果で更新される。</summary>
    public string? LastResult { get; private set; }

    /// <summary>直近のバックグラウンド整形成功結果。</summary>
    public string? LastFormattedResult { get; private set; }

    public FormattingMode? ActiveMode { get; private set; }

    public DictationController(DictationPipeline pipeline, AppSettings settings)
    {
        _pipeline = pipeline;
        _settings = settings;
        _pipeline.BackgroundFormattingCompleted += OnBackgroundFormattingCompleted;
    }

    private void OnBackgroundFormattingCompleted(BackgroundFormattingResult result)
    {
        lock (_gate)
        {
            if (result.FormattedText is { } formatted)
            {
                LastFormattedResult = formatted;
                LastResult = formatted;
            }
        }

        BackgroundFormattingCompleted?.Invoke(result);
    }

    /// <summary>ホットキー押下。Idleなら録音開始、Recordingなら停止して後段処理へ。</summary>
    public void Toggle(FormattingMode mode)
    {
        lock (_gate)
        {
            switch (_state)
            {
                case State.Idle:
                    _state = State.Recording;
                    ActiveMode = mode;
                    _stopRecordingCts = new CancellationTokenSource();
                    _ = RunPipelineAsync(mode, _stopRecordingCts.Token);
                    break;

                case State.Recording:
                    _state = State.Processing;
                    _stopRecordingCts?.Cancel();
                    break;

                case State.Processing:
                    // 処理中の押下は無視(多重実行防止)
                    break;
            }
        }
    }

    private async Task RunPipelineAsync(FormattingMode mode, CancellationToken stopRecording)
    {
        PipelineResult result;
        try
        {
            result = await _pipeline
                .RunAsync(mode, stopRecording, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new PipelineResult(false, null, false, false, false, ex.Message);
        }

        lock (_gate)
        {
            _state = State.Idle;
            ActiveMode = null;
            _stopRecordingCts?.Dispose();
            _stopRecordingCts = null;
            if (result is { Success: true, Text: not null })
            {
                LastResult = result.Text;
            }
        }

        Completed?.Invoke(result);
    }
}
