using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace FeatherScribe.App;

/// <summary>
/// FeatherScribe 以外で最後に前面だったウィンドウ (直前の入力先) を記録し、貼り付け前に戻す。
/// タスクバーやデスクトップなどのシェル画面は入力先として記録しない。
/// App (コンポジションルート) が1つだけ生成し、終了時に破棄する。
/// </summary>
public sealed class ForegroundWindowTracker : IDisposable
{
    // Shell surfaces that become foreground when the user clicks the tray icon, the taskbar,
    // the desktop, Start/Search or the notification overflow. They are never an input target.
    private static readonly HashSet<string> IgnoredWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "Progman",
        "WorkerW",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
    };

    private const int ClassNameCapacity = 256;

    private readonly DispatcherTimer _timer;
    private IntPtr _lastExternalWindow;

    public ForegroundWindowTracker()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _timer.Tick += (_, _) => CaptureForegroundWindow();
        _timer.Start();
    }

    /// <summary>
    /// Last recorded external window (zero when none). Read-only: RecordingOverlay uses it to pick
    /// the monitor of the user's input target; only the tracker's own timer updates it.
    /// </summary>
    internal IntPtr LastExternalWindow => _lastExternalWindow;

    /// <summary>True when the class name belongs to a shell surface that must not be a paste target.</summary>
    internal static bool IsIgnoredWindowClass(string className)
        => !string.IsNullOrEmpty(className) && IgnoredWindowClasses.Contains(className);

    /// <summary>True when the current foreground window belongs to the FeatherScribe process.</summary>
    public static bool IsCurrentProcessForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        return processId == Environment.ProcessId;
    }

    public bool TryRestoreLastExternalWindow()
    {
        if (_lastExternalWindow == IntPtr.Zero ||
            !IsWindow(_lastExternalWindow) ||
            !IsWindowVisible(_lastExternalWindow))
        {
            return false;
        }

        return SetForegroundWindow(_lastExternalWindow);
    }

    private void CaptureForegroundWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !IsWindowVisible(foreground))
        {
            return;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return;
        }

        if (IsIgnoredWindowClass(GetWindowClassName(foreground)))
        {
            return;
        }

        _lastExternalWindow = foreground;
    }

    private static string GetWindowClassName(IntPtr hWnd)
    {
        var buffer = new StringBuilder(ClassNameCapacity);
        var length = GetClassName(hWnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : "";
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);
}
