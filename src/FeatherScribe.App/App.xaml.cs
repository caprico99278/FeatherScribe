using System.Net.Http;
using System.Windows;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.App;

/// <summary>
/// コンポジションルート。設定読み込み → パイプライン組み立て → 常駐開始。
/// </summary>
public partial class App : Application
{
    private HttpClient? _httpClient;
    private HotkeyService? _hotkeyService;
    private TrayIconService? _trayIconService;
    private RecordingOverlay? _overlay;
    private MainWindow? _mainWindow;
    private DictationPipeline? _pipeline;
    private FileEventLog? _eventLog;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var rootPath = AppRoot.Locate();
        var settingsProvider = new JsonAppSettingsProvider(rootPath);
        var settings = settingsProvider.Load();

        var dictionaryProvider = new JsonDictionaryProvider(rootPath);
        _httpClient = new HttpClient();

        var textOutput = new ClipboardTextOutput(settings.Output);
        _eventLog = new FileEventLog(rootPath);
        _pipeline = new DictationPipeline(
            new NAudioRecorder(settings.Recording),
            new WhisperCppTranscriptionEngine(settings.Asr),
            new OllamaGemmaFormatter(_httpClient, settings.Llm, new FilePromptProvider(rootPath)),
            new DictionaryCorrector(dictionaryProvider.Load()),
            textOutput,
            dictionaryProvider,
            _eventLog,
            settings);

        var controller = new DictationController(_pipeline, settings);

        _overlay = new RecordingOverlay();
        _mainWindow = new MainWindow(controller, settings, textOutput);
        _trayIconService = new TrayIconService(_mainWindow);
        _hotkeyService = new HotkeyService();

        WireEvents(_pipeline, controller);

        // 登録失敗は該当キーのみ無効化し、起動は必ず継続する (指示書002 §2)
        var hotkeyReport = _hotkeyService.RegisterFromSettings(settings.Hotkeys, controller.Toggle);

        _mainWindow.ShowHotkeyReport(hotkeyReport);
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

        if (hotkeyReport.HasFailures)
        {
            foreach (var failure in hotkeyReport.Failed)
            {
                _eventLog.Write(new PipelineEvent(
                    DateTimeOffset.Now, "hotkey_register", false,
                    $"{failure.Mode}:{failure.HotkeyText}:{failure.Reason}",
                    0, failure.Mode.ToString(), null, 0));
            }

            _trayIconService.Notify(
                "一部ホットキー登録に失敗しました",
                string.Join("\n", hotkeyReport.Failed.Select(f => $"{f.Mode}: {f.HotkeyText} ({f.Reason})")) +
                "\n該当キーのみ無効です。config/appsettings.json の hotkeys で変更できます。");
        }
    }

    private void WireEvents(DictationPipeline pipeline, DictationController controller)
    {
        pipeline.StageChanged += (stage, message) => Dispatcher.Invoke(() =>
        {
            switch (stage)
            {
                case PipelineStage.Recording:
                    _overlay!.ShowStatus("● 録音中", isRecording: true);
                    break;
                case PipelineStage.Transcribing:
                    _overlay!.ShowStatus("文字起こし中…", isRecording: false);
                    break;
                case PipelineStage.Formatting:
                    _overlay!.ShowStatus("整形中…", isRecording: false);
                    break;
                case PipelineStage.Outputting:
                    _overlay!.ShowStatus("貼り付け中…", isRecording: false);
                    break;
                case PipelineStage.Completed or PipelineStage.Failed:
                    _overlay!.HideStatus();
                    break;
            }

            _mainWindow!.UpdateStage(stage, message);
        });

        controller.Completed += result => Dispatcher.Invoke(() =>
        {
            _mainWindow!.UpdateResult(result);

            if (!result.Success)
            {
                _trayIconService!.Notify("FeatherScribe エラー", result.ErrorMessage ?? "処理に失敗しました");
            }
            else if (result.UsedFallback)
            {
                // 指示書§12.2: 整形失敗/タイムアウトが分かる通知を出す
                _trayIconService!.Notify(
                    "整形失敗・未整形で貼り付け",
                    result.ErrorMessage ?? "整形に失敗/タイムアウトしたため raw transcript を使用しました");
            }
            else if (!result.OutputSucceeded)
            {
                _trayIconService!.Notify(
                    "貼り付け失敗",
                    "結果はアプリ内に保持しています。メイン画面から再コピーしてください。");
            }
        });

        // バックグラウンド整形の完了通知 (ワーカースレッドから来るためDispatcherへ)
        controller.BackgroundFormattingCompleted += result => Dispatcher.Invoke(() =>
        {
            _mainWindow!.UpdateBackgroundFormatting(result);

            if (result.FormattedText is not null)
            {
                // 自動置換はしない。ユーザー操作 (再コピー/再貼り付け) でのみ利用可能。
                _trayIconService!.Notify(
                    "整形完了",
                    "整形結果を「再コピー」「再コピー+貼り付け」で利用できます(自動置換はしません)。");
            }
            else
            {
                _trayIconService!.Notify(
                    "バックグラウンド整形に失敗しました",
                    $"{result.ErrorMessage}\nraw transcriptは貼り付け済みです。");
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();
        _pipeline?.Dispose(); // 実行中のバックグラウンド整形を安全にキャンセル
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
