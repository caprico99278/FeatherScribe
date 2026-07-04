using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public class HotkeyRegistrationTests
{
    /// <summary>指定したホットキー文字列だけ失敗させるfake。</summary>
    private sealed class FakeRegistrar(
        Func<int, HotkeyDefinition, (bool Success, int Win32Error)>? handler = null) : IHotkeyRegistrar
    {
        public List<int> RegisteredIds { get; } = [];

        public bool TryRegister(int id, HotkeyDefinition definition, out int win32Error)
        {
            var (success, error) = handler?.Invoke(id, definition) ?? (true, 0);
            win32Error = error;
            if (success)
            {
                RegisteredIds.Add(id);
            }

            return success;
        }
    }

    private sealed class ThrowingRegistrar : IHotkeyRegistrar
    {
        public bool TryRegister(int id, HotkeyDefinition definition, out int win32Error)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void RegisterAll_AllSucceed_SevenModesRegistered()
    {
        var registrar = new FakeRegistrar();

        var report = HotkeyRegistrationPlanner.RegisterAll(new HotkeySettings(), registrar);

        Assert.False(report.HasFailures);
        Assert.Equal(7, report.Registered.Count);
        Assert.Contains(report.Registered, r => r.Mode == FormattingMode.NoFormat && r.HotkeyText == "Ctrl+Shift+F8");
        Assert.Contains(report.Registered, r => r.Mode == FormattingMode.Memo && r.HotkeyText == "Ctrl+Alt+Shift+M");
    }

    [Fact]
    public void RegisterAll_SomeFail_OthersRemainUsable_NoException()
    {
        // ERROR_HOTKEY_ALREADY_REGISTERED 相当を PlainFast だけ返す
        var settings = new HotkeySettings();
        var registrar = new FakeRegistrar((_, definition) =>
            definition.VirtualKey == 0x78 // F9 (PlainFast)
                ? (false, HotkeyRegistrationPlanner.ErrorHotkeyAlreadyRegistered)
                : (true, 0));

        var report = HotkeyRegistrationPlanner.RegisterAll(settings, registrar);

        Assert.True(report.HasFailures);
        Assert.Equal(6, report.Registered.Count);
        var failure = Assert.Single(report.Failed);
        Assert.Equal(FormattingMode.PlainFast, failure.Mode);
        Assert.Equal("Ctrl+Shift+F9", failure.HotkeyText);
        Assert.Contains("他アプリが登録済み", failure.Reason);
        Assert.Contains("1409", failure.Reason);
    }

    [Fact]
    public void RegisterAll_AllFail_ReturnsFailuresWithoutThrowing()
    {
        var registrar = new FakeRegistrar((_, _) => (false, 1409));

        var report = HotkeyRegistrationPlanner.RegisterAll(new HotkeySettings(), registrar);

        Assert.Empty(report.Registered);
        Assert.Equal(7, report.Failed.Count);
    }

    [Fact]
    public void RegisterAll_RegistrarThrows_CapturedAsFailure()
    {
        var report = HotkeyRegistrationPlanner.RegisterAll(new HotkeySettings(), new ThrowingRegistrar());

        Assert.Equal(7, report.Failed.Count);
        Assert.All(report.Failed, f => Assert.Contains("例外", f.Reason));
    }

    [Fact]
    public void RegisterAll_InvalidHotkeyText_ReportedAsParseFailure()
    {
        var settings = new HotkeySettings { PlainFast = "NotAHotkey+++" };
        var registrar = new FakeRegistrar();

        var report = HotkeyRegistrationPlanner.RegisterAll(settings, registrar);

        var failure = Assert.Single(report.Failed);
        Assert.Equal(FormattingMode.PlainFast, failure.Mode);
        Assert.Equal("書式不正", failure.Reason);
        Assert.Equal(6, report.Registered.Count);
    }
}
