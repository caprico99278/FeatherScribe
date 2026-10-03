using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.App;

/// <summary>
/// コンポジションルート。設定読み込み → パイプライン組み立て → 常駐開始。
/// </summary>
public partial class App : Application
{
    // Identifier (not user-facing) of the selected-text editing hotkey in logs and the startup tray notice.
    private const string EditSelectionActionId = "EditSelection";

    private HttpClient? _httpClient;
    private HotkeyService? _hotkeyService;
    private TrayIconService? _trayIconService;
    private RecordingOverlay? _overlay;
    private MainWindow? _mainWindow;
    private DictationPipeline? _pipeline;
    private FileEventLog? _eventLog;
    private ForegroundWindowTracker? _foregroundWindowTracker;
    private NAudioRecorder? _recorder;
    private SelectionEditService? _selectionEditService;

    // Ollama server started by the launcher (--owned-ollama-pid): stopped when FeatherScribe exits.
    private int? _ownedOllamaPid;
    private string? _rootPath;

    // Recording meter coalescing: the audio thread stores only the latest level and posts
    // at most one pending UI update at a time (visual_direction.md §24).
    private float _latestAudioLevel;
    private int _levelUpdatePending;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var rootPath = AppRoot.Locate();
        var settingsProvider = new JsonAppSettingsProvider(rootPath);

        // Launcher overrides (FeatherScribe.cmd): in memory only, config files are not written.
        var overrides = LaunchOverrides.Parse(e.Args);
        var settings = overrides.Apply(settingsProvider.Load());
        _rootPath = rootPath;
        _ownedOllamaPid = overrides.OwnedOllamaPid;

        var dictionaryProvider = new JsonDictionaryProvider(rootPath);
        _httpClient = new HttpClient();

        // One tracker for the whole app: the explicit repaste and the pipeline paste guard
        // share the same "previous input target".
        var foregroundWindowTracker = new ForegroundWindowTracker();
        _foregroundWindowTracker = foregroundWindowTracker;

        var textOutput = new ClipboardTextOutput(settings.Output);

        // The pipeline never sends Ctrl+V into FeatherScribe's own window (e.g. MainWindow opened
        // while recording). MainWindow keeps the inner output for its explicit repaste path,
        // which restores the target itself.
        var pipelineOutput = new PasteTargetGuardTextOutput(
            textOutput,
            isOwnProcessForeground: () => Dispatcher.Invoke(() => ForegroundWindowTracker.IsCurrentProcessForeground()),
            tryRestoreExternalTarget: () => Dispatcher.Invoke(() => foregroundWindowTracker.TryRestoreLastExternalWindow()));

        _eventLog = new FileEventLog(rootPath);
        _recorder = new NAudioRecorder(settings.Recording);

        // Shared by the dictation pipeline and selected-text editing (same prompts, validator and timeouts).
        var formatter = new OllamaGemmaFormatter(_httpClient, settings.Llm, new FilePromptProvider(rootPath), _eventLog);
        _pipeline = new DictationPipeline(
            _recorder,
            new WhisperCppTranscriptionEngine(settings.Asr),
            formatter,
            new DictionaryCorrector(dictionaryProvider.Load()),
            pipelineOutput,
            dictionaryProvider,
            _eventLog,
            settings);

        var controller = new DictationController(_pipeline, settings);

        // The overlay reads the tracker's last external window to appear on the input target's monitor.
        _overlay = new RecordingOverlay(foregroundWindowTracker);
        _mainWindow = new MainWindow(controller, settings, textOutput, foregroundWindowTracker);
        _trayIconService = new TrayIconService(_mainWindow);
        _hotkeyService = new HotkeyService();

        WireEvents(_pipeline, controller);
        _recorder.AudioLevelChanged += OnAudioLevelChanged;

        // Selected-text editing (Phase UX-1): its own clipboard restore, independent of output.restoreClipboard.
        _selectionEditService = new SelectionEditService(
            new WpfClipboardAccess(),
            new SystemSelectionKeyboard(),
            formatter,
            dictionaryProvider,
            SystemForegroundWindow.Get,
            ForegroundWindowTracker.IsCurrentProcessForeground,
            () => controller.IsBusy,
            settings,
            eventLog: _eventLog);
        _selectionEditService.FormattingStarted += () => Dispatcher.InvokeAsync(() =>
            _overlay?.ShowPresentation(OverlayPresentationMapper.FromSelectionEditFormatting()));

        // 登録失敗は該当キーのみ無効化し、起動は必ず継続する (指示書002 §2)
        var hotkeyReport = _hotkeyService.RegisterFromSettings(settings.Hotkeys, controller.Toggle);
        var editSelectionHotkey = _hotkeyService.RegisterAction(
            UserFacingText.SelectionEditActionLabel,
            settings.Hotkeys.EditSelection,
            () => _ = RunSelectionEditAsync());

        _mainWindow.ShowHotkeyReport(hotkeyReport, [editSelectionHotkey]);
        _mainWindow.Show();

        if (settingsProvider.LastWarnings.Count > 0)
        {
            foreach (var warning in settingsProvider.LastWarnings)
            {
                _eventLog.Write(new PipelineEvent(
                    DateTimeOffset.Now, "settings_warning", false,
                    warning, 0, "Startup", null, 0));
            }

            _trayIconService.Notify(
                "設定読み込み警告",
                string.Join("\n", settingsProvider.LastWarnings));
        }

        if (settingsProvider.LastError is not null)
        {
            _trayIconService.Notify(
                "設定読み込みエラー",
                $"既定値で起動しました: {settingsProvider.LastError}");
        }

        if (hotkeyReport.HasFailures || editSelectionHotkey.Failed)
        {
            foreach (var failure in hotkeyReport.Failed)
            {
                _eventLog.Write(new PipelineEvent(
                    DateTimeOffset.Now, "hotkey_register", false,
                    $"{failure.Mode}:{failure.HotkeyText}:{failure.Reason}",
                    0, failure.Mode.ToString(), null, 0));
            }

            var failureLines = hotkeyReport.Failed.Select(f => $"{f.Mode}: {f.HotkeyText} ({f.Reason})").ToList();
            if (editSelectionHotkey.Failed)
            {
                _eventLog.Write(new PipelineEvent(
                    DateTimeOffset.Now, "hotkey_register", false,
                    $"{EditSelectionActionId}:{editSelectionHotkey.HotkeyText}:{editSelectionHotkey.FailureReason}",
                    0, EditSelectionActionId, null, 0));
                failureLines.Add($"{EditSelectionActionId}: {editSelectionHotkey.HotkeyText} ({editSelectionHotkey.FailureReason})");
            }

            _trayIconService.Notify(
                "一部ホットキー登録に失敗しました",
                string.Join("\n", failureLines) +
                "\n該当キーのみ無効です。config/appsettings.json の hotkeys で変更できます。");
        }
    }

    /// <summary>
    /// Selection edit hotkey (UI thread). The service never throws and runs off the UI thread after its
    /// guards; the outcome goes to the overlay and, when the user has something to act on, the tray.
    /// </summary>
    private async Task RunSelectionEditAsync()
    {
        if (_selectionEditService is not { } service)
        {
            return;
        }

        SelectionEditOutcome outcome;
        try
        {
            outcome = await service.RunAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Not expected (the service maps every failure to an outcome); never crash the hotkey handler.
            Debug.WriteLine($"[App] selection edit failed unexpectedly: {ex.GetType().Name}");
            return;
        }

        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _overlay?.ShowPresentation(OverlayPresentationMapper.FromSelectionEditOutcome(outcome));
                if (UserFacingText.SelectionEditTrayNotice(outcome) is { } notice)
                {
                    _trayIconService?.Notify(notice.Title, notice.Body);
                }
            });
        }
        catch (Exception ex)
        {
            // Dispatcher unavailable (shutting down).
            Debug.WriteLine($"[App] selection edit notice failed: {ex.GetType().Name}");
        }
    }

    private void WireEvents(DictationPipeline pipeline, DictationController controller)
    {
        pipeline.StageChanged += (stage, message) => Dispatcher.Invoke(() =>
        {
            var presentation = OverlayPresentationMapper.FromStage(stage);
            if (presentation.State != OverlayVisualState.Hidden)
            {
                _overlay!.ShowPresentation(presentation);
            }

            _mainWindow!.UpdateStage(stage, message);
        });

        controller.Completed += result => Dispatcher.Invoke(() =>
        {
            _mainWindow!.UpdateResult(result);
            _overlay!.ShowPresentation(OverlayPresentationMapper.FromPipelineResult(result));

            // 失敗 → 貼り付け失敗 → 整形失敗(指示書§12.2) の順。StatusText / Overlay と同じ優先度。
            if (UserFacingText.CompletionNotice(result) is { } notice)
            {
                _trayIconService!.Notify(notice.Title, notice.Body);
            }
        });

        // 録音上限による自動停止: 文字起こし開始時に一度だけ知らせる (以後は通常の完了表示)。
        // Raised right after the Transcribing stage, so this text replaces 「文字起こし中」.
        controller.RecordingLimitReached += notice => Dispatcher.Invoke(() =>
        {
            _overlay!.ShowPresentation(OverlayPresentationMapper.FromRecordingLimitReached(notice));
            var trayNotice = UserFacingText.RecordingLimitTrayNotice(notice.LimitSeconds);
            _trayIconService!.Notify(trayNotice.Title, trayNotice.Body);
        });

        // バックグラウンド整形の完了通知 (ワーカースレッドから来るためDispatcherへ)
        // rawPasted: the raw text of this operation is known to be pasted (never claimed otherwise).
        controller.BackgroundFormattingCompleted += (result, rawPasted) => Dispatcher.Invoke(() =>
        {
            _mainWindow!.UpdateBackgroundFormatting(result, rawPasted);
            _overlay!.ShowPresentation(OverlayPresentationMapper.FromBackgroundFormattingResult(result));

            if (result.FormattedText is not null)
            {
                // 自動置換はしない。ユーザー操作 (コピー/貼り付け) でのみ利用可能。
                _trayIconService!.Notify(
                    UserFacingText.NotifyBackgroundFormattedTitle,
                    UserFacingText.NotifyBackgroundFormattedBody);
            }
            else if (UserFacingText.IsRejectedCandidate(result))
            {
                _trayIconService!.Notify(
                    UserFacingText.NotifyBackgroundRejectedTitle,
                    UserFacingText.NotifyBackgroundRejectedBody(result, rawPasted));
            }
            else
            {
                _trayIconService!.Notify(
                    UserFacingText.NotifyBackgroundFailedTitle,
                    UserFacingText.NotifyBackgroundFailedBodyFor(rawPasted));
            }
        });
    }

    /// <summary>
    /// Audio callback thread. Never blocks: stores the latest level and posts one UI update
    /// only when none is pending, so updates never queue up behind a busy UI thread.
    /// </summary>
    private void OnAudioLevelChanged(float level)
    {
        Volatile.Write(ref _latestAudioLevel, level);
        if (Interlocked.Exchange(ref _levelUpdatePending, 1) == 0)
        {
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(ApplyLatestAudioLevel));
            }
            catch (Exception)
            {
                // Dispatcher unavailable (shutting down): the meter just stays still.
                Volatile.Write(ref _levelUpdatePending, 0);
            }
        }
    }

    private void ApplyLatestAudioLevel()
    {
        // Reset first so a level arriving while this runs schedules the next update.
        Volatile.Write(ref _levelUpdatePending, 0);
        _overlay?.SetAudioLevel(Volatile.Read(ref _latestAudioLevel));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_recorder is not null)
        {
            _recorder.AudioLevelChanged -= OnAudioLevelChanged;
        }

        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();
        _pipeline?.Dispose(); // 実行中のバックグラウンド整形を安全にキャンセル
        _foregroundWindowTracker?.Dispose();
        _httpClient?.Dispose();
        StopOwnedOllama();
        base.OnExit(e);
    }

    /// <summary>
    /// Stops the Ollama server the launcher started for this session (tray 終了 and window close both end
    /// here). A server that was already running is never passed to the app, so it is never stopped.
    /// </summary>
    private void StopOwnedOllama()
    {
        if (_ownedOllamaPid is not { } pid || _rootPath is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var errorType = OwnedOllamaProcess.TryStop(pid, OwnedOllamaProcess.ExpectedExecutablePath(_rootPath));
        _eventLog?.Write(new PipelineEvent(
            DateTimeOffset.Now, "ollama_stop", errorType is null,
            errorType, stopwatch.ElapsedMilliseconds, "Shutdown", null, 0));
    }
}
