using System.ComponentModel;
using System.Windows;
using FeatherScribe.Core;

namespace FeatherScribe.App;

/// <summary>
/// 状態表示と直近結果の再コピー/再貼り付けを行う最小限のメイン画面。
/// 閉じるボタンはトレイへの格納として扱う。
/// </summary>
public partial class MainWindow : Window
{
    private readonly DictationController _controller;
    private readonly ITextOutput _output;

    public MainWindow(DictationController controller, AppSettings settings, ITextOutput output)
    {
        InitializeComponent();
        _controller = controller;
        _output = output;

        var llmState = settings.Llm.Enabled ? "有効" : "無効 (llm.enabled=false / 全モード未整形)";
        HotkeyHelpText.Text =
            $"ホットキー(押して録音開始、もう一度押して停止) / LLM整形: {llmState}\n" +
            $"  {settings.Hotkeys.NoFormat} : NoFormat(最速・整形なし)\n" +
            $"  {settings.Hotkeys.PlainFast} : PlainFast(軽量整形) / " +
            $"{settings.Hotkeys.PlainQuality} : PlainQuality(高品質)\n" +
            $"  {settings.Hotkeys.Polite} : Polite / {settings.Hotkeys.Memo} : Memo / " +
            $"{settings.Hotkeys.Bullet} : Bullet / {settings.Hotkeys.DevInstruction} : DevInstruction";
    }

    public void UpdateStage(PipelineStage stage, string? message)
    {
        StatusText.Text = stage switch
        {
            PipelineStage.Recording => $"録音中… ({_controller.ActiveMode})",
            PipelineStage.Transcribing => "文字起こし中…",
            PipelineStage.Formatting => "Gemma 4 で整形中…",
            PipelineStage.Outputting => "出力中…",
            PipelineStage.Completed => message is null ? "完了" : $"完了({message})",
            PipelineStage.Failed => $"失敗: {message}",
            _ => StatusText.Text,
        };
    }

    public void UpdateResult(PipelineResult result)
    {
        if (result is { Success: true, Text: not null })
        {
            LastResultText.Text = result.Text;
            RecopyButton.IsEnabled = true;
            RepasteButton.IsEnabled = true;
            if (result.BackgroundFormattingStarted)
            {
                StatusText.Text = "raw貼り付け済み・バックグラウンドで整形中…";
            }
        }
        else
        {
            StatusText.Text = $"失敗: {result.ErrorMessage}";
        }
    }

    /// <summary>バックグラウンド整形の完了 (成功時は直近結果を整形テキストへ更新)。</summary>
    public void UpdateBackgroundFormatting(BackgroundFormattingResult result)
    {
        if (result.FormattedText is { } formatted)
        {
            LastResultText.Text = formatted;
            RecopyButton.IsEnabled = true;
            RepasteButton.IsEnabled = true;
            StatusText.Text = "整形完了・再コピー/再貼り付けで整形結果を利用できます";
        }
        else
        {
            StatusText.Text = $"バックグラウンド整形失敗 (raw貼り付け済み): {result.ErrorMessage}";
        }
    }

    /// <summary>ホットキー登録結果を画面に表示する (失敗キーは後からここで確認できる)。</summary>
    public void ShowHotkeyReport(HotkeyRegistrationReport report)
    {
        if (!report.HasFailures)
        {
            return;
        }

        HotkeyHelpText.Text +=
            "\n⚠ 登録失敗 (無効): " +
            string.Join(" / ", report.Failed.Select(f => $"{f.Mode}: {f.HotkeyText} ({f.Reason})"));
    }

    public async Task RecopyAsync()
    {
        if (_controller.LastResult is { } text)
        {
            await _output.OutputAsync(text, OutputMode.ClipboardOnly, CancellationToken.None);
            StatusText.Text = "直近結果をクリップボードへコピーしました";
        }
    }

    public async Task RepasteAsync()
    {
        if (_controller.LastResult is { } text)
        {
            await _output.OutputAsync(text, OutputMode.ClipboardAndPaste, CancellationToken.None);
        }
    }

    private async void RecopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RecopyAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"コピー失敗: {ex.Message}";
        }
    }

    private async void RepasteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RepasteAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"貼り付け失敗: {ex.Message}";
        }
    }

    /// <summary>トレイの「終了」からのみ実際に閉じる。</summary>
    public void CloseForExit()
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
    }
}
