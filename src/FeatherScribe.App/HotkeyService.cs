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
    private readonly Dictionary<int, Action> _idToAction = [];
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

    /// <summary>
    /// Registers one extra named action hotkey (e.g. selected-text editing) through the same parser and
    /// registrar as the modes. Never throws; a failure is returned for the MainWindow/tray report.
    /// An empty hotkey text means the action is disabled (not registered, not a failure).
    /// </summary>
    public HotkeyActionRegistration RegisterAction(string name, string? hotkeyText, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var id = HotkeyActionRegistration.FirstActionId + _idToAction.Count;
        var registration = HotkeyActionRegistration.Register(this, id, name, hotkeyText);
        if (registration.IsRegistered)
        {
            _idToAction[id] = callback;
        }

        return registration;
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
        else if (msg == WmHotkey && _idToAction.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
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

        foreach (var id in _idToAction.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _idToMode.Clear();
        _idToAction.Clear();
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>
/// Result of registering one extra named action hotkey. Same parser, registrar and failure reasons as
/// <see cref="HotkeyRegistrationPlanner"/>; action ids start above the mode ids so they never collide.
/// </summary>
/// <param name="Name">User-facing action label shown in the failure line.</param>
/// <param name="HotkeyText">The configured hotkey text.</param>
/// <param name="Id">Registered id, or 0 when not registered.</param>
/// <param name="FailureReason">Why registration failed; null when registered or disabled.</param>
public sealed record HotkeyActionRegistration(string Name, string HotkeyText, int Id, string? FailureReason)
{
    public const int FirstActionId = 100;

    public bool IsRegistered => Id != 0 && FailureReason is null;

    public bool IsDisabled => Id == 0 && FailureReason is null;

    public bool Failed => FailureReason is not null;

    public static HotkeyActionRegistration Register(IHotkeyRegistrar registrar, int id, string name, string? hotkeyText)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentOutOfRangeException.ThrowIfLessThan(id, FirstActionId);
        var text = hotkeyText ?? "";

        if (string.IsNullOrWhiteSpace(text))
        {
            return new HotkeyActionRegistration(name, text, 0, null);
        }

        if (!HotkeyParser.TryParse(text, out var definition))
        {
            return new HotkeyActionRegistration(name, text, 0, "書式不正");
        }

        bool success;
        int win32Error;
        try
        {
            success = registrar.TryRegister(id, definition, out win32Error);
        }
        catch (Exception ex)
        {
            return new HotkeyActionRegistration(name, text, 0, $"例外: {ex.Message}");
        }

        if (!success)
        {
            var reason = win32Error == HotkeyRegistrationPlanner.ErrorHotkeyAlreadyRegistered
                ? $"他アプリが登録済み (Win32Error={win32Error})"
                : $"Win32Error={win32Error}";
            return new HotkeyActionRegistration(name, text, 0, reason);
        }

        return new HotkeyActionRegistration(name, text, id, null);
    }
}
