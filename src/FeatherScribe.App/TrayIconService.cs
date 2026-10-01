using System.Diagnostics;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace FeatherScribe.App;

/// <summary>
/// タスクトレイ常駐アイコン。表示/再整形/コピー/貼り付け/終了のメニューと通知を提供する。
/// 結果操作は MainWindow の公開エントリ (例外を外へ出さない) をそのまま呼ぶ。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const string IconResourceUri = "pack://application:,,,/Assets/Icons/FeatherScribe.ico";

    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly MainWindow _mainWindow;
    private readonly WinForms.ToolStripItem _reformatItem;
    private readonly WinForms.ToolStripItem _copyItem;
    private readonly WinForms.ToolStripItem _pasteItem;

    public TrayIconService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _trayIcon = LoadTrayIcon();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(UserFacingText.TrayShowWindow, null, (_, _) => ShowMainWindow());
        _reformatItem = menu.Items.Add(UserFacingText.TrayReformat, null, (_, _) => _mainWindow.StartReformat());
        _copyItem = menu.Items.Add(UserFacingText.TrayCopyLatest, null, async (_, _) => await _mainWindow.CopyLatestAsync());
        _pasteItem = menu.Items.Add(UserFacingText.TrayPasteLatest, null, async (_, _) => await _mainWindow.PasteLatestToPreviousTargetAsync());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(UserFacingText.TrayExit, null, (_, _) => ExitApplication());
        menu.Opening += (_, _) => RefreshMenuAvailability();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = UserFacingText.TrayToolTip,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    public void Notify(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Warning);
    }

    /// <summary>
    /// Offers each result action only when it can run now, with the same meaning as the
    /// MainWindow buttons. 「画面を表示」 and 「終了」 are always enabled.
    /// </summary>
    private void RefreshMenuAvailability()
    {
        try
        {
            var availability = _mainWindow.GetActionAvailability();
            _reformatItem.Enabled = availability.CanReformat;
            _copyItem.Enabled = availability.CanCopy;
            _pasteItem.Enabled = availability.CanRepaste;
        }
        catch (Exception ex)
        {
            // Opening the menu must never crash the app; the items keep their previous state.
            Debug.WriteLine($"[TrayIconService] Availability refresh failed: {ex}");
        }
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
