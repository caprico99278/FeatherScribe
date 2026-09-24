using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace FeatherScribe.GuiTests;

public sealed class FlaUiMainWindowTests
{
    [Fact]
    public void MainWindow_FlaUiContract_DoesNotScrollAtStartupOrWhenSectionsExpandAt720Width()
    {
        var appPath = FindAppExecutable();
        using var app = Application.Launch(appPath);
        using var automation = new UIA3Automation();

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
            Assert.NotNull(window);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
            AssertFitsWithoutScrolling(window, "startup");

            ResizeWindowWidth(window, width: 720);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(250));

            AssertRequiredControlsBeforeExpansion(window);
            AssertVisibleButtonsInitialState(window);
            AssertFitsWithoutScrolling(window, "startup at 720 width");

            SetExpandedByClick(window, "CandidateExpander", expanded: true);
            AssertFitsWithoutScrolling(window, "candidate expanded");
            SetExpandedByClick(window, "CandidateExpander", expanded: false);

            SetExpandedByClick(window, "OperationGuideExpander", expanded: true);
            AssertFitsWithoutScrolling(window, "operation guide expanded");

            SetExpandedByClick(window, "CandidateExpander", expanded: true);
            AssertRequiredControlsAfterExpansion(window);
            AssertCandidateButtonInitialState(window);
            AssertFitsWithoutScrolling(window, "both expanded");

            var hotkeyHelp = FindRequired(window, "HotkeyHelpText");
            Assert.False(hotkeyHelp.Properties.IsOffscreen.Value);

            SetExpandedByClick(window, "CandidateExpander", expanded: false);
            SetExpandedByClick(window, "OperationGuideExpander", expanded: false);
            AssertFitsWithoutScrolling(window, "both collapsed again");

            window.Focus();
            Assert.True(window.Properties.HasKeyboardFocus.Value || window.Properties.IsKeyboardFocusable.Value);
        }
        finally
        {
            app.Close();
            if (!app.HasExited)
            {
                app.Kill();
            }
        }
    }

    private static void AssertRequiredControlsBeforeExpansion(Window window)
    {
        var requiredAutomationIds = new[]
        {
            "MainContentScrollViewer",
            "StatusText",
            "LastResultText",
            "CandidateExpander",
            "OperationGuideExpander",
            "ReformatButton",
            "RecopyButton",
            "RepasteButton",
        };

        foreach (var automationId in requiredAutomationIds)
        {
            Assert.NotNull(FindRequired(window, automationId));
        }
    }

    private static void AssertRequiredControlsAfterExpansion(Window window)
    {
        Assert.NotNull(FindRequired(window, "RejectedResultText"));
        Assert.NotNull(FindRequired(window, "AdoptRejectedButton"));
        Assert.NotNull(FindRequired(window, "HotkeyHelpText"));
    }

    private static void AssertVisibleButtonsInitialState(Window window)
    {
        Assert.False(FindRequired(window, "ReformatButton").AsButton().IsEnabled);
        Assert.False(FindRequired(window, "RecopyButton").AsButton().IsEnabled);
        Assert.False(FindRequired(window, "RepasteButton").AsButton().IsEnabled);
    }

    private static void AssertCandidateButtonInitialState(Window window)
    {
        Assert.False(FindRequired(window, "AdoptRejectedButton").AsButton().IsEnabled);
    }

    private static void SetExpandedByClick(Window window, string automationId, bool expanded)
    {
        var element = FindRequired(window, automationId);
        Assert.True(element.Patterns.ExpandCollapse.IsSupported, $"{automationId} must support ExpandCollapsePattern.");
        var expected = expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        if (element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == expected)
        {
            return;
        }

        var headerButton = element.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button))
            ?? throw new InvalidOperationException($"{automationId} header button was not found.");
        if (headerButton.Patterns.Toggle.IsSupported)
        {
            headerButton.Patterns.Toggle.Pattern.Toggle();
        }
        else
        {
            headerButton.Click();
        }

        // Expander motion plus the window height refit.
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(400));
        Assert.Equal(expected, element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value);
    }

    /// <summary>
    /// The page must not scroll. The only allowed exception is a screen too small for the
    /// content, where the window has already grown to the full work-area height.
    /// </summary>
    private static void AssertFitsWithoutScrolling(Window window, string state)
    {
        var handle = window.Properties.NativeWindowHandle.Value;
        Assert.True(GetWindowRect(handle, out var rect));
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Assert.True(GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref monitorInfo));
        var work = monitorInfo.WorkArea;

        Assert.True(rect.Top >= work.Top && rect.Bottom <= work.Bottom, $"[{state}] window must stay inside the work area.");

        var scroll = FindRequired(window, "MainContentScrollViewer").Patterns.Scroll.Pattern;
        if (scroll.VerticallyScrollable.Value)
        {
            var windowHeight = rect.Bottom - rect.Top;
            var workHeight = work.Bottom - work.Top;
            Assert.True(
                windowHeight >= workHeight - 2,
                $"[{state}] MainContentScrollViewer scrolls although the window ({windowHeight}px) is shorter than the work area ({workHeight}px).");
        }
    }

    private static AutomationElement FindRequired(Window window, string automationId)
        => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
           ?? throw new InvalidOperationException($"Missing AutomationId: {automationId}");

    private static string FindAppExecutable()
    {
        var root = FindRepoRoot();
        var exePath = Path.Combine(
            root,
            "src",
            "FeatherScribe.App",
            "bin",
            "Debug",
            "net10.0-windows",
            "FeatherScribe.App.exe");

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("FeatherScribe.App must be built before running FlaUI tests.", exePath);
        }

        return exePath;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FeatherScribe.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FeatherScribe repository root.");
    }

    private static void ResizeWindowWidth(Window window, int width)
    {
        var handle = Process.GetProcessById(window.Properties.ProcessId.Value).MainWindowHandle;
        Assert.NotEqual(IntPtr.Zero, handle);
        Assert.True(GetWindowRect(handle, out var rect));
        Assert.True(SetWindowPos(handle, IntPtr.Zero, 0, 0, width, rect.Bottom - rect.Top, SetWindowPositionFlags.NoZOrder | SetWindowPositionFlags.NoMove));
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        SetWindowPositionFlags uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [Flags]
    private enum SetWindowPositionFlags : uint
    {
        NoMove = 0x0002,
        NoZOrder = 0x0004,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
