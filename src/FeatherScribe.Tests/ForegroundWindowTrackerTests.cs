using FeatherScribe.App;

namespace FeatherScribe.Tests;

public sealed class ForegroundWindowTrackerTests
{
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("NotifyIconOverflowWindow")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("XamlExplorerHostIslandWindow")]
    public void IsIgnoredWindowClass_ShellSurfaces_AreIgnored(string className)
    {
        Assert.True(ForegroundWindowTracker.IsIgnoredWindowClass(className));
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("CabinetWClass")]
    [InlineData("ApplicationFrameWindow")]
    [InlineData("")]
    public void IsIgnoredWindowClass_NormalWindows_AreNotIgnored(string className)
    {
        Assert.False(ForegroundWindowTracker.IsIgnoredWindowClass(className));
    }
}
