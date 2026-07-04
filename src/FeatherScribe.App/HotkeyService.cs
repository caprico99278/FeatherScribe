using System.Runtime.InteropServices;
using System.Windows.Interop;
using FeatherScribe.Core;

namespace FeatherScribe.App;

/// <summary>
/// Win32 RegisterHotKey によるグローバルホットキー。
/// 専用の非表示ウィンドウで WM_HOTKEY を受け取る。
/// 登録失敗 (他アプリとの衝突等) は該当キーのみ無効化し、決してアプリを落とさない。
/// </summary>
public sealed class HotkeyService : IDisposable, IHotkeyRegistrar
{
    private const int WmHotkey = 0x0312;

    private readonly HwndSource _source;
    private readonly Dictionary<int, FormattingMode> _idToMode = [];
    private Action<FormattingMode>? _callback;

    /// <summary>直近の登録結果 (成功/失敗一覧)。UI表示・後からの確認用。</summary>
    public HotkeyRegistrationReport? LastReport { get; private set; }

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("FeatherScribeHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>設定からホットキーを登録する。失敗しても例外を出さず、結果レポートを返す。</summary>
    public HotkeyRegistrationReport RegisterFromSettings(
        HotkeySettings hotkeys,
        Action<FormattingMode> callback)
    {
        _callback = callback;
        var report = HotkeyRegistrationPlanner.RegisterAll(hotkeys, this);
        foreach (var registration in report.Registered)
        {
            _idToMode[registration.Id] = registration.Mode;
        }

        LastReport = report;
        return report;
    }

    bool IHotkeyRegistrar.TryRegister(int id, HotkeyDefinition definition, out int win32Error)
    {
        if (RegisterHotKey(_source.Handle, id, (uint)definition.Modifiers, definition.VirtualKey))
        {
            win32Error = 0;
            return true;
        }

        win32Error = Marshal.GetLastWin32Error();
        return false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _idToMode.TryGetValue(wParam.ToInt32(), out var mode))
        {
            _callback?.Invoke(mode);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _idToMode.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _idToMode.Clear();
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
