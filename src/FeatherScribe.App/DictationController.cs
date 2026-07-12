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
    private Guid _latestOperationId;
    private FormattingMode? _lastFormattingMode;
    private bool _lastBackgroundFormattingRejected;

    public event Action<PipelineResult>? Completed;

    /// <summary>バックグラウンド整形の完了通知 (成功/失敗)。</summary>
    public event Action<BackgroundFormattingResult>? BackgroundFormattingCompleted;

    /// <summary>直近結果 (コピー/貼り付け用)。バックグラウンド整形成功時は整形結果で更新される。</summary>
    public string? LastResult { get; private set; }

    /// <summary>再整形用の直近raw結果。バックグラウンド整形成功後もrawを保持する。</summary>
    public string? LastRawResult { get; private set; }

    /// <summary>直近のバックグラウンド整形成功結果。</summary>
    public string? LastFormattedResult { get; private set; }

    /// <summary>Validatorにより自動採用されなかった直近候補。ユーザー確認後の手動採用用。</summary>
    public string? LastRejectedFormattedResult { get; private set; }

    public FormattingMode? ActiveMode { get; private set; }

    public DictationController(DictationPipeline pipeline, AppSettings settings)
    {
        _pipeline = pipeline;
        _settings = settings;
        _pipeline.BackgroundFormattingCompleted += OnBackgroundFormattingCompleted;
    }

    private void OnBackgroundFormattingCompleted(BackgroundFormattingResult result)
    {
        var shouldPublish = true;
        lock (_gate)
        {
            if (result.OperationId != _latestOperationId)
            {
                shouldPublish = false;
            }
            else if (result.FormattedText is { } formatted)
            {
                LastFormattedResult = formatted;
                LastResult = formatted;
                LastRejectedFormattedResult = null;
                _lastBackgroundFormattingRejected = false;
            }
            else
            {
                LastRejectedFormattedResult = result.RejectedText;
                _lastBackgroundFormattingRejected = true;
            }
        }

        if (shouldPublish)
        {
            BackgroundFormattingCompleted?.Invoke(result);
        }
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
                    _latestOperationId = Guid.NewGuid();
                    _lastFormattingMode = mode;
                    _lastBackgroundFormattingRejected = false;
                    LastFormattedResult = null;
                    LastRejectedFormattedResult = null;
                    _ = RunPipelineAsync(mode, _stopRecordingCts.Token, _latestOperationId);
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

    private async Task RunPipelineAsync(FormattingMode mode, CancellationToken stopRecording, Guid operationId)
    {
        PipelineResult result;
        try
        {
            result = await _pipeline
                .RunAsync(mode, stopRecording, CancellationToken.None, operationId)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new PipelineResult(false, null, false, false, false, ex.Message, operationId);
        }

        var shouldPublish = true;
        lock (_gate)
        {
            if (result.OperationId != _latestOperationId)
            {
                shouldPublish = false;
            }
            else
            {
                _state = State.Idle;
                ActiveMode = null;
                _stopRecordingCts?.Dispose();
                _stopRecordingCts = null;
                if (result is { Success: true, Text: not null })
                {
                    LastResult = result.Text;
                    LastRawResult = result.Text;
                    _lastFormattingMode = mode;
                }
            }
        }

        if (shouldPublish)
        {
            Completed?.Invoke(result);
        }
    }

    public FormattingMode? ReformatLast()
    {
        lock (_gate)
        {
            if (_state != State.Idle ||
                LastRawResult is not { } raw ||
                _lastFormattingMode is not { } mode ||
                mode == FormattingMode.NoFormat ||
                !_settings.Llm.Enabled)
            {
                return null;
            }

            var retryMode = _lastBackgroundFormattingRejected && mode == FormattingMode.PlainFast
                ? FormattingMode.PlainQuality
                : mode;

            _latestOperationId = Guid.NewGuid();
            _lastFormattingMode = retryMode;
            _lastBackgroundFormattingRejected = false;
            LastFormattedResult = null;
            _pipeline.QueueBackgroundFormatting(_latestOperationId, raw, retryMode);
            return retryMode;
        }
    }

    public bool AdoptRejectedFormattedResult()
    {
        lock (_gate)
        {
            if (LastRejectedFormattedResult is not { } rejected)
            {
                return false;
            }

            LastResult = rejected;
            LastFormattedResult = rejected;
            LastRejectedFormattedResult = null;
            _lastBackgroundFormattingRejected = false;
            return true;
        }
    }
}
