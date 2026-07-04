using System.Windows;
using WinForms = System.Windows.Forms;

namespace FeatherScribe.App;

/// <summary>
/// タスクトレイ常駐アイコン。表示/再コピー/再貼り付け/終了のメニューと通知を提供する。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly MainWindow _mainWindow;

    public TrayIconService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("画面を表示", null, (_, _) => ShowMainWindow());
        menu.Items.Add("直近結果を再コピー", null, async (_, _) => await SafeAsync(_mainWindow.RecopyAsync));
        menu.Items.Add("直近結果を再貼り付け", null, async (_, _) => await SafeAsync(_mainWindow.RepasteAsync));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitApplication());

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
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

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}
