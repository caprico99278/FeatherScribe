using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace FeatherScribe.App;

/// <summary>
/// FeatherScribe 以外で最後に前面だったウィンドウを記録し、貼り付け前に戻す。
/// </summary>
public sealed class ForegroundWindowTracker : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly int _currentProcessId = Environment.ProcessId;
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
        if (processId == 0 || processId == _currentProcessId)
        {
            return;
        }

        _lastExternalWindow = foreground;
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
}
