namespace FeatherScribe.Core;

/// <summary>
/// OSへのホットキー登録の抽象。テストでは失敗を返すfakeに差し替える。
/// </summary>
public interface IHotkeyRegistrar
{
    /// <returns>成功時 true。失敗時は win32Error に GetLastError 相当のコード。</returns>
    bool TryRegister(int id, HotkeyDefinition definition, out int win32Error);
}

public sealed record HotkeyRegistration(FormattingMode Mode, string HotkeyText, int Id);

public sealed record HotkeyRegistrationFailure(FormattingMode Mode, string HotkeyText, string Reason);

public sealed record HotkeyRegistrationReport(
    IReadOnlyList<HotkeyRegistration> Registered,
    IReadOnlyList<HotkeyRegistrationFailure> Failed)
{
    public bool HasFailures => Failed.Count > 0;
}

/// <summary>
/// 設定の全ホットキーを登録する。個々の失敗 (書式不正・他アプリとの衝突) は
/// 失敗一覧として返し、決して例外でアプリ起動を止めない (指示書002 §2)。
/// </summary>
public static class HotkeyRegistrationPlanner
{
    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED (想定内の衝突)。</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    public static HotkeyRegistrationReport RegisterAll(
        HotkeySettings hotkeys,
        IHotkeyRegistrar registrar,
        Action<FormattingMode, int>? onRegistered = null)
    {
        var registered = new List<HotkeyRegistration>();
        var failed = new List<HotkeyRegistrationFailure>();
        var nextId = 1;

        foreach (var (mode, text) in EnumerateBindings(hotkeys))
        {
            if (!HotkeyParser.TryParse(text, out var definition))
            {
                failed.Add(new HotkeyRegistrationFailure(mode, text, "書式不正"));
                continue;
            }

            var id = nextId++;
            bool success;
            int win32Error;
            try
            {
                success = registrar.TryRegister(id, definition, out win32Error);
            }
            catch (Exception ex)
            {
                failed.Add(new HotkeyRegistrationFailure(mode, text, $"例外: {ex.Message}"));
                continue;
            }

            if (!success)
            {
                var reason = win32Error == ErrorHotkeyAlreadyRegistered
                    ? $"他アプリが登録済み (Win32Error={win32Error})"
                    : $"Win32Error={win32Error}";
                failed.Add(new HotkeyRegistrationFailure(mode, text, reason));
                continue;
            }

            registered.Add(new HotkeyRegistration(mode, text, id));
            onRegistered?.Invoke(mode, id);
        }

        return new HotkeyRegistrationReport(registered, failed);
    }

    private static IEnumerable<(FormattingMode Mode, string Text)> EnumerateBindings(HotkeySettings hotkeys)
    {
        yield return (FormattingMode.NoFormat, hotkeys.NoFormat);
        yield return (FormattingMode.PlainFast, hotkeys.PlainFast);
        yield return (FormattingMode.PlainQuality, hotkeys.PlainQuality);
        yield return (FormattingMode.Polite, hotkeys.Polite);
        yield return (FormattingMode.Memo, hotkeys.Memo);
        yield return (FormattingMode.DevInstruction, hotkeys.DevInstruction);
        yield return (FormattingMode.Bullet, hotkeys.Bullet);
    }
}
