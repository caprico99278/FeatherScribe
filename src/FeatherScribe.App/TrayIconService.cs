using System.Windows;
using WinForms = System.Windows.Forms;

namespace FeatherScribe.App;

/// <summary>
/// タスクトレイ常駐アイコン。表示/コピー/貼り付け/終了のメニューと通知を提供する。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const string IconResourceUri = "pack://application:,,,/Assets/Icons/FeatherScribe.ico";

    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly MainWindow _mainWindow;

    public TrayIconService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _trayIcon = LoadTrayIcon();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("画面を表示", null, (_, _) => ShowMainWindow());
        menu.Items.Add("直近rawをもう一度整形", null, (_, _) => _mainWindow.ReformatLast());
        menu.Items.Add("直近結果をクリップボードにコピー", null, async (_, _) => await SafeAsync(_mainWindow.RecopyAsync));
        menu.Items.Add("直近結果を直前の入力先へ貼り付け", null, async (_, _) => await SafeAsync(_mainWindow.RepasteAsync));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitApplication());

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "FeatherScribe - ローカル音声入力",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    public void Notify(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Warning);
    }

    private void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        _mainWindow.CloseForExit();
        Application.Current.Shutdown();
    }

    private static async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            // トレイメニュー操作の失敗でアプリを落とさない
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = Application.GetResourceStream(new Uri(IconResourceUri, UriKind.Absolute))
            ?? throw new InvalidOperationException($"Icon resource not found: {IconResourceUri}");

        // Icon copies the stream contents, so the resource stream can be closed right away.
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
    }
}
