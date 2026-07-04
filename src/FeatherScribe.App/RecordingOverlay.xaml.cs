using System.Windows;
using System.Windows.Media;

namespace FeatherScribe.App;

/// <summary>
/// 画面下部に表示する録音中/処理中インジケータ。フォーカスを奪わない。
/// </summary>
public partial class RecordingOverlay : Window
{
    private static readonly Brush RecordingBrush =
        new SolidColorBrush(Color.FromArgb(0xDD, 0xB0, 0x20, 0x20));
    private static readonly Brush ProcessingBrush =
        new SolidColorBrush(Color.FromArgb(0xDD, 0x22, 0x22, 0x22));

    public RecordingOverlay()
    {
        InitializeComponent();
    }

    public void ShowStatus(string text, bool isRecording)
    {
        OverlayText.Text = text;
        Pill.Background = isRecording ? RecordingBrush : ProcessingBrush;

        var workArea = SystemParameters.WorkArea;
        Show();
        UpdateLayout();
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Bottom - ActualHeight - 24;
    }

    public void HideStatus()
    {
        Hide();
    }
}
