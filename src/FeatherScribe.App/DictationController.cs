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

    // Whether the output of LastRawResult was pasted, and the operation (dictation, or a reformat of
    // the same raw text) that state applies to. Any other operation id means "unknown".
    private bool _lastRawPasted;
    private Guid _rawPastedOperationId;

    public event Action<PipelineResult>? Completed;

    /// <summary>
    /// バックグラウンド整形の完了通知 (成功/失敗)。第2引数は、その操作の未整形文章が貼り付け済みと
    /// 確認できているか (貼り付け失敗・不明時は false。UI は貼り付け済みと表示してはならない)。
    /// </summary>
    public event Action<BackgroundFormattingResult, bool>? BackgroundFormattingCompleted;

    /// <summary>
    /// The recording limit stopped the latest dictation's recording (raised once, when processing starts).
    /// The controller has moved from Recording to Processing (as after a user stop), so hotkey presses during
    /// processing are ignored and the next press after completion starts a new recording. Raised from a worker thread.
    /// </summary>
    public event Action<RecordingLimitNotice>? RecordingLimitReached;

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
        _pipeline.RecordingLimitReached += OnRecordingLimitReached;
    }

    private void OnRecordingLimitReached(RecordingLimitNotice notice)
    {
        lock (_gate)
        {
            if (notice.OperationId != _latestOperationId)
            {
                return;
            }

            // The recorder stopped by itself: the dictation is processing now, the same state as after a
            // user stop. Otherwise the user's next hotkey press would be consumed as "stop" and lost.
            // Nothing is cancelled; _stopRecordingCts is disposed when the run completes, as usual.
            if (_state == State.Recording)
            {
                _state = State.Processing;
            }
        }

        RecordingLimitReached?.Invoke(notice);
    }

    private void OnBackgroundFormattingCompleted(BackgroundFormattingResult result)
    {
        var shouldPublish = true;
        bool rawPasted;
        lock (_gate)
        {
            // Unknown (e.g. the background result arrived before the dictation result) → not pasted.
            rawPasted = _rawPastedOperationId == result.OperationId && _lastRawPasted;
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
            BackgroundFormattingCompleted?.Invoke(result, rawPasted);
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
                    _lastRawPasted = result.OutputSucceeded;
                    _rawPastedOperationId = operationId;
                    _lastFormattingMode = mode;
                }
            }
        }

        if (shouldPublish)
        {
            Completed?.Invoke(result);
        }
    }

    /// <summary>True while a dictation is recording or processing (read-only; used by selected-text editing).</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _state != State.Idle;
            }
        }
    }

    /// <summary>True iff <see cref="ReformatLast"/> would start a reformat now (same predicate).</summary>
    public bool CanReformat
    {
        get
        {
            lock (_gate)
            {
                return CanReformatCore(out _, out _);
            }
        }
    }

    /// <summary>True when there is a latest result to copy or paste.</summary>
    public bool HasLatestResult
    {
        get
        {
            lock (_gate)
            {
                return LastResult is not null;
            }
        }
    }

    /// <summary>True when a rejected formatting candidate can be adopted.</summary>
    public bool HasRejectedCandidate
    {
        get
        {
            lock (_gate)
            {
                return LastRejectedFormattedResult is not null;
            }
        }
    }

    // Must be called under _gate. Shared by CanReformat and ReformatLast so they never diverge.
    private bool CanReformatCore(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? raw,
        out FormattingMode mode)
    {
        raw = LastRawResult;
        mode = _lastFormattingMode ?? FormattingMode.NoFormat;
        return _state == State.Idle &&
            raw is not null &&
            _lastFormattingMode is not null &&
            mode != FormattingMode.NoFormat &&
            _settings.Llm.Enabled;
    }

    public FormattingMode? ReformatLast()
    {
        lock (_gate)
        {
            if (!CanReformatCore(out var raw, out var mode))
            {
                return null;
            }

            var retryMode = _lastBackgroundFormattingRejected && mode == FormattingMode.PlainFast
                ? FormattingMode.PlainQuality
                : mode;

            _latestOperationId = Guid.NewGuid();
            // A reformat uses the same raw text, so its paste state carries over (always recorded
            // together with LastRawResult).
            _rawPastedOperationId = _latestOperationId;
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
