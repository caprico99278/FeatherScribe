using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace FeatherScribe.Tests;

public sealed class FlaUiMainWindowTests
{
    private const string RunGuiTestsEnvironmentVariable = "FEATHERSCRIBE_RUN_GUI_TESTS";
    private const double NoScroll = -1;

    [Fact]
    public void MainWindow_FlaUiContract_CanExpandBothSectionsAndScrollAt720By480()
    {
        if (!ShouldRunGuiTests())
        {
            return;
        }

        var appPath = FindAppExecutable();
        using var app = Application.Launch(appPath);
        using var automation = new UIA3Automation();

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
            Assert.NotNull(window);

            ResizeWindow(window, width: 720, height: 480);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(250));

            AssertRequiredControlsBeforeExpansion(window);
            AssertVisibleButtonsInitialState(window);

            ExpandByClick(window, "OperationGuideExpander");
            ExpandByClick(window, "CandidateExpander");
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(250));
            AssertRequiredControlsAfterExpansion(window);
            AssertCandidateButtonInitialState(window);

            var scrollViewer = FindRequired(window, "MainContentScrollViewer");
            Assert.True(scrollViewer.Patterns.Scroll.IsSupported, "MainContentScrollViewer must support ScrollPattern.");

            var scroll = scrollViewer.Patterns.Scroll.Pattern;
            Assert.True(scroll.VerticallyScrollable.Value, "MainContentScrollViewer must be vertically scrollable with both sections expanded at 720x480.");

            scroll.SetScrollPercent(NoScroll, 100);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(250));

            var hotkeyHelp = FindRequired(window, "HotkeyHelpText");
            Assert.False(hotkeyHelp.Properties.IsOffscreen.Value);

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

    private static bool ShouldRunGuiTests()
        => string.Equals(
            Environment.GetEnvironmentVariable(RunGuiTestsEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

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

    private static void ExpandByClick(Window window, string automationId)
    {
        var element = FindRequired(window, automationId);
        Assert.True(element.Patterns.ExpandCollapse.IsSupported, $"{automationId} must support ExpandCollapsePattern.");
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
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(250));
        Assert.Equal(ExpandCollapseState.Expanded, element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value);
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

    private static void ResizeWindow(Window window, int width, int height)
    {
        var handle = Process.GetProcessById(window.Properties.ProcessId.Value).MainWindowHandle;
        Assert.NotEqual(IntPtr.Zero, handle);
        Assert.True(SetWindowPos(handle, IntPtr.Zero, 0, 0, width, height, SetWindowPositionFlags.NoZOrder | SetWindowPositionFlags.NoMove));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        SetWindowPositionFlags uFlags);

    [Flags]
    private enum SetWindowPositionFlags : uint
    {
        NoMove = 0x0002,
        NoZOrder = 0x0004,
    }
}
