using System.Diagnostics;

namespace FeatherScribe.Core;

public enum PipelineStage
{
    Recording,
    Transcribing,
    Formatting,
    Outputting,
    Completed,
    Failed,
}

/// <param name="Text">出力(貼り付け)されたテキスト。失敗時も再コピー用に保持されることがある。</param>
/// <param name="BackgroundFormattingStarted">raw先貼り付け後、整形をバックグラウンドで開始したか。
/// 結果は <see cref="DictationPipeline.BackgroundFormattingCompleted"/> で通知される。</param>
public sealed record PipelineResult(
    bool Success,
    string? Text,
    bool BackgroundFormattingStarted,
    bool UsedFallback,
    bool OutputSucceeded,
    string? ErrorMessage);

/// <param name="FormattedText">整形済みテキスト。失敗時は null。</param>
public sealed record BackgroundFormattingResult(
    FormattingMode Mode,
    string? FormattedText,
    string? ErrorMessage);

/// <summary>
/// 録音 → 文字起こし → 辞書補正(前) → LLM整形 → 辞書補正(後) → 出力 の縦パイプライン。
/// - LLM整形は llm.enabled=true の場合のみ実行する (既定OFF)。
/// - PlainFast/PlainQualityでは raw を貼り付けた時点でパイプラインを完了扱いにし、
///   整形は別Taskでバックグラウンド実行する (次の録音をブロックしない)。自動置換はしない。
/// - LLM整形の失敗は raw transcript へフォールバックし、パイプラインは止めない(指示書§12.2)。
/// - Dispose でバックグラウンド整形を安全にキャンセルする (アプリ終了時)。
/// </summary>
public sealed class DictationPipeline : IDisposable
{
    private readonly IAudioRecorder _recorder;
    private readonly ISpeechToTextEngine _speechToText;
    private readonly ITextFormatter _formatter;
    private readonly IDictionaryCorrector _corrector;
    private readonly ITextOutput _output;
    private readonly IDictionaryProvider _dictionaryProvider;
    private readonly IEventLog _eventLog;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetimeCts = new();

    public event Action<PipelineStage, string?>? StageChanged;

    /// <summary>バックグラウンド整形の完了 (成功/失敗) 通知。ワーカースレッドから発火する。</summary>
    public event Action<BackgroundFormattingResult>? BackgroundFormattingCompleted;

    /// <summary>直近のバックグラウンド整形成功結果 (再コピー/再貼り付け用)。</summary>
    public string? LastBackgroundFormattedText { get; private set; }

    public DictationPipeline(
        IAudioRecorder recorder,
        ISpeechToTextEngine speechToText,
        ITextFormatter formatter,
        IDictionaryCorrector corrector,
        ITextOutput output,
        IDictionaryProvider dictionaryProvider,
        IEventLog eventLog,
        AppSettings settings)
    {
        _recorder = recorder;
        _speechToText = speechToText;
        _formatter = formatter;
        _corrector = corrector;
        _output = output;
        _dictionaryProvider = dictionaryProvider;
        _eventLog = eventLog;
        _settings = settings;
    }

    /// <param name="mode">整形モード。</param>
    /// <param name="stopRecording">キャンセルされると録音を停止し後段処理へ進む。</param>
    /// <param name="cancellationToken">パイプライン全体の中断。</param>
    public async Task<PipelineResult> RunAsync(
        FormattingMode mode,
        CancellationToken stopRecording,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string? audioPath = null;

        try
        {
            // 1. 録音
            StageChanged?.Invoke(PipelineStage.Recording, null);
            var recorded = await _recorder.RecordUntilStoppedAsync(stopRecording).ConfigureAwait(false);
            audioPath = recorded.File.Path;
            cancellationToken.ThrowIfCancellationRequested();

            // 2. 文字起こし
            StageChanged?.Invoke(PipelineStage.Transcribing, null);
            var transcription = await _speechToText
                .TranscribeAsync(recorded.File, cancellationToken)
                .ConfigureAwait(false);

            if (!transcription.IsSuccess || string.IsNullOrWhiteSpace(transcription.RawText))
            {
                var error = transcription.ErrorMessage ?? "empty_transcript";
                LogEvent("transcribe", false, error, stopwatch.ElapsedMilliseconds, mode, charCount: 0);
                StageChanged?.Invoke(PipelineStage.Failed, error);
                // 指示書§12.1: ASR失敗時はクリップボードを変更しない
                return new PipelineResult(false, null, false, false, false, $"文字起こしに失敗しました: {error}");
            }

            LogEvent("transcribe", true, null, stopwatch.ElapsedMilliseconds, mode, transcription.RawText.Length);

            // 3. 辞書補正(前)
            var corrected = _corrector.Correct(transcription.RawText.Trim());

            // 4. LLM無効、またはNoFormatなら即出力
            if (mode == FormattingMode.NoFormat || !_settings.Llm.Enabled)
            {
                var (ok, outputError) = await TryOutputAsync(corrected, cancellationToken).ConfigureAwait(false);
                LogEvent("output", ok, outputError, stopwatch.ElapsedMilliseconds, mode, corrected.Length);
                var note = mode != FormattingMode.NoFormat && !_settings.Llm.Enabled
                    ? "LLM整形が無効設定 (llm.enabled=false) のため未整形で出力しました"
                    : null;
                StageChanged?.Invoke(PipelineStage.Completed, note);
                return new PipelineResult(true, corrected, false, false, ok, note ?? outputError);
            }

            var request = new FormatRequest(corrected, mode, _dictionaryProvider.Load());

            // 5a. raw先貼り付け + バックグラウンド整形 (PlainFast/PlainQuality)
            //     raw貼り付け時点でパイプラインは完了扱い。整形は待たない。
            var rawFirst = _settings.Llm.RawFirstPaste
                && mode is FormattingMode.PlainFast or FormattingMode.PlainQuality;
            if (rawFirst)
            {
                StageChanged?.Invoke(PipelineStage.Outputting, null);
                var (ok, outputError) = await TryOutputAsync(corrected, cancellationToken).ConfigureAwait(false);
                LogEvent("output", ok, outputError, stopwatch.ElapsedMilliseconds, mode, corrected.Length);

                StartBackgroundFormatting(request);
                StageChanged?.Invoke(PipelineStage.Completed, "raw貼り付け完了・バックグラウンドで整形中");
                return new PipelineResult(true, corrected, true, false, ok, outputError);
            }

            // 5b. 整形してから出力 (Polite/Bullet/Memo/DevInstruction、またはrawFirst無効時)
            StageChanged?.Invoke(PipelineStage.Formatting, null);
            var formatResult = await _formatter.FormatAsync(request, cancellationToken).ConfigureAwait(false);
            LogEvent("format", !formatResult.UsedFallback, formatResult.ErrorMessage,
                stopwatch.ElapsedMilliseconds, mode, formatResult.Text.Length);

            if (formatResult.UsedFallback && !_settings.Llm.FallbackToRaw)
            {
                // fallback禁止設定: 出力せず、結果は再コピー用に保持
                StageChanged?.Invoke(PipelineStage.Failed, formatResult.ErrorMessage);
                return new PipelineResult(false, corrected, false, true, false,
                    formatResult.ErrorMessage ?? "整形に失敗しました (fallbackToRaw=false)");
            }

            var finalText = _corrector.Correct(formatResult.Text.Trim());

            StageChanged?.Invoke(PipelineStage.Outputting, null);
            var (outputOk, finalOutputError) = await TryOutputAsync(finalText, cancellationToken).ConfigureAwait(false);
            LogEvent("output", outputOk, finalOutputError, stopwatch.ElapsedMilliseconds, mode, finalText.Length);

            StageChanged?.Invoke(PipelineStage.Completed,
                formatResult.UsedFallback ? "整形失敗・未整形で出力しました" : null);
            return new PipelineResult(
                true, finalText, false, formatResult.UsedFallback, outputOk,
                formatResult.ErrorMessage ?? finalOutputError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogEvent("pipeline", false, "canceled", stopwatch.ElapsedMilliseconds, mode, 0);
            StageChanged?.Invoke(PipelineStage.Failed, "キャンセルされました");
            return new PipelineResult(false, null, false, false, false, "キャンセルされました");
        }
        catch (Exception ex)
        {
            LogEvent("pipeline", false, ex.GetType().Name, stopwatch.ElapsedMilliseconds, mode, 0);
            StageChanged?.Invoke(PipelineStage.Failed, ex.Message);
            return new PipelineResult(false, null, false, false, false, ex.Message);
        }
        finally
        {
            CleanUpAudio(audioPath);
        }
    }

    /// <summary>
    /// バックグラウンド整形を開始する (fire-and-forget)。
    /// パイプライン本体・次の録音をブロックしない。失敗してもアプリを落とさず、
    /// アプリ終了時 (Dispose) には安全にキャンセルされる。
    /// </summary>
    private void StartBackgroundFormatting(FormatRequest request)
    {
        var lifetimeToken = _lifetimeCts.Token;
        _ = Task.Run(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await _formatter.FormatAsync(request, lifetimeToken).ConfigureAwait(false);
                lifetimeToken.ThrowIfCancellationRequested();

                if (result.UsedFallback)
                {
                    LogEvent("format_background", false, result.ErrorMessage,
                        stopwatch.ElapsedMilliseconds, request.Mode, 0);
                    BackgroundFormattingCompleted?.Invoke(new BackgroundFormattingResult(
                        request.Mode, null, result.ErrorMessage ?? "整形に失敗しました"));
                    return;
                }

                var formatted = _corrector.Correct(result.Text.Trim());
                LastBackgroundFormattedText = formatted;
                LogEvent("format_background", true, null,
                    stopwatch.ElapsedMilliseconds, request.Mode, formatted.Length);
                BackgroundFormattingCompleted?.Invoke(new BackgroundFormattingResult(
                    request.Mode, formatted, null));
            }
            catch (OperationCanceledException)
            {
                // アプリ終了によるキャンセルは静かに終了
            }
            catch (Exception ex)
            {
                LogEvent("format_background", false, ex.GetType().Name,
                    stopwatch.ElapsedMilliseconds, request.Mode, 0);
                BackgroundFormattingCompleted?.Invoke(new BackgroundFormattingResult(
                    request.Mode, null, ex.Message));
            }
        }, CancellationToken.None);
    }

    /// <summary>出力失敗でもパイプラインを落とさない(指示書§12.3/§12.4)。</summary>
    private async Task<(bool Ok, string? Error)> TryOutputAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await _output
                .OutputAsync(text, _settings.Output.ParsedMode, cancellationToken)
                .ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message);
        }
    }

    private void CleanUpAudio(string? audioPath)
    {
        // プライバシー方針(指示書§11): デバッグモードで明示的に許可しない限り音声を残さない
        if (audioPath is null)
        {
            return;
        }

        var keep = _settings.Debug.AllowContentSaving && _settings.Privacy.SaveAudioFiles;
        if (keep)
        {
            return;
        }

        try
        {
            if (File.Exists(audioPath))
            {
                File.Delete(audioPath);
            }
        }
        catch (IOException)
        {
            // 削除失敗でパイプラインを落とさない(次回起動時の一時領域掃除に委ねる)
        }
    }

    private void LogEvent(
        string stage,
        bool success,
        string? errorType,
        long elapsedMilliseconds,
        FormattingMode mode,
        int charCount)
    {
        _eventLog.Write(new PipelineEvent(
            DateTimeOffset.Now,
            stage,
            success,
            errorType,
            elapsedMilliseconds,
            mode.ToString(),
            _settings.Llm.Enabled ? _settings.Llm.Model : null,
            charCount));
    }

    /// <summary>アプリ終了時に呼び、実行中のバックグラウンド整形を安全にキャンセルする。</summary>
    public void Dispose()
    {
        try
        {
            _lifetimeCts.Cancel();
        }
        finally
        {
            _lifetimeCts.Dispose();
        }
    }
}
