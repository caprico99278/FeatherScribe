using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
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
            AssertFeedbackNotShown(window, "startup");

            ResizeWindowWidth(window, width: 720);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(250));

            AssertRequiredControlsBeforeExpansion(window);
            AssertVisibleButtonsInitialState(window);
            AssertFitsWithoutScrolling(window, "startup at 720 width");
            AssertFeedbackNotShown(window, "startup at 720 width");

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

            EnsureForeground(window, automation);
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

    [Fact]
    public void MainWindow_FlaUiContract_ActionsDisabledAtStartupAndTabReachesResultThenCandidateHeader()
    {
        var appPath = FindAppExecutable();

        // Keys are only sent while FeatherScribe owns the foreground. If another process takes
        // the foreground mid-sequence, the sequence restarts from a fresh launch: the startup
        // focus on the window root is the defined start state, and UI Automation cannot move
        // focus back to the window root once a child element has it.
        for (var sequence = 0; sequence <= MaxKeyboardSequenceRetries; sequence++)
        {
            using var app = Application.Launch(appPath);
            using var automation = new UIA3Automation();

            try
            {
                var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
                Assert.NotNull(window);
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

                // No result yet: nothing to copy, paste, reformat or adopt.
                AssertVisibleButtonsInitialState(window);

                var lastResult = FindRequired(window, "LastResultText");
                var candidateHeader = FindRequired(window, "CandidateExpander")
                    .FindFirstDescendant(cf => cf.ByControlType(ControlType.Button))
                    ?? throw new InvalidOperationException("CandidateExpander header button was not found.");

                EnsureForeground(window, automation);
                Assert.True(
                    PollUntil(() => window.Properties.HasKeyboardFocus.ValueOrDefault, FocusPollTimeout),
                    $"Precondition failed: the window root must have keyboard focus before the first Tab, but focus is on '{DescribeFocus(automation)}'.");
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

                // StatusText is not a tab stop: the first Tab lands on the latest result.
                if (!TryTabTo(window, automation, lastResult, "First Tab must focus LastResultText"))
                {
                    continue;
                }

                if (!TryTabTo(window, automation, candidateHeader, "Second Tab must focus the CandidateExpander header"))
                {
                    continue;
                }

                AssertFitsWithoutScrolling(window, "after keyboard navigation");
                return;
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

        Assert.Fail(
            $"Precondition failed: another process took the foreground during each of {MaxKeyboardSequenceRetries + 1} "
            + $"keyboard sequences. Foreground owner: {DescribeForegroundWindow()}.");
    }

    [Fact]
    public void MainWindow_FlaUiContract_DisabledActionsAreReportedDisabledAndStayVisibleAt720Width()
    {
        var appPath = FindAppExecutable();
        using var app = Application.Launch(appPath);
        using var automation = new UIA3Automation();

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
            Assert.NotNull(window);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

            ResizeWindowWidth(window, width: 720);
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(250));
            SetExpandedByClick(window, "CandidateExpander", expanded: true);

            // Phase UI-8: disabled buttons have readable text (no opacity), are exposed as
            // disabled to UI Automation, and are laid out on screen inside the window.
            var windowRect = window.BoundingRectangle;
            foreach (var automationId in new[] { "ReformatButton", "RecopyButton", "RepasteButton", "AdoptRejectedButton" })
            {
                var button = FindRequired(window, automationId);
                Assert.False(button.Properties.IsEnabled.Value, $"{automationId} must be disabled before any result.");
                Assert.False(button.Properties.IsOffscreen.Value, $"{automationId} must stay visible while disabled.");
                var rect = button.BoundingRectangle;
                Assert.True(rect.Width > 0 && rect.Height > 0, $"{automationId} must have a laid-out size.");
                Assert.True(
                    rect.Left >= windowRect.Left && rect.Right <= windowRect.Right,
                    $"{automationId} must not be clipped horizontally at 720 width.");
            }

            // The page never scrolls horizontally, and does not scroll vertically when it fits.
            var scroll = FindRequired(window, "MainContentScrollViewer").Patterns.Scroll.Pattern;
            Assert.False(scroll.HorizontallyScrollable.Value);
            AssertFitsWithoutScrolling(window, "candidate expanded with disabled actions at 720 width");
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

    private const int ForegroundAttempts = 5;
    private const int MaxKeyboardSequenceRetries = 2;
    private static readonly TimeSpan ForegroundPollTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FocusPollTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Precondition for any input or focus-dependent assert: the FeatherScribe window is the
    /// foreground window and UIA focus was set on its root. Windows silently refuses
    /// SetForegroundWindow (foreground lock) while another process owns the foreground and the
    /// test process did not receive the last input, so a plain SetForeground() is not enough.
    /// Each attempt calls FlaUI SetForeground(); if that is refused, the standard minimal
    /// workaround is applied (<see cref="ForceForegroundWindow"/>). Fails as a precondition,
    /// naming the foreground owner, instead of letting a later focus-order assert fail.
    /// </summary>
    private static void EnsureForeground(Window window, AutomationBase automation)
    {
        var handle = window.Properties.NativeWindowHandle.Value;
        for (var attempt = 1; attempt <= ForegroundAttempts; attempt++)
        {
            window.SetForeground();
            if (GetForegroundWindow() != handle)
            {
                ForceForegroundWindow(handle);
            }

            if (PollUntil(() => GetForegroundWindow() == handle && TryFocus(window), ForegroundPollTimeout))
            {
                return;
            }
        }

        Assert.Fail(
            $"Precondition failed: FeatherScribe could not be brought to the foreground after {ForegroundAttempts} attempts, "
            + $"so no input was sent. Foreground owner: {DescribeForegroundWindow()}. Focus: '{DescribeFocus(automation)}'.");
    }

    /// <summary>
    /// Standard foreground-lock workaround: restore a minimized window, then temporarily attach
    /// this thread to the foreground window's input queue so SetForegroundWindow is permitted.
    /// AttachThreadInput is used rather than a synthetic ALT key so that no keystroke can ever
    /// reach the application that currently owns the foreground.
    /// </summary>
    private static void ForceForegroundWindow(IntPtr handle)
    {
        if (IsIconic(handle))
        {
            ShowWindow(handle, ShowWindowRestore);
        }

        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static bool TryFocus(AutomationElement element)
    {
        try
        {
            element.Focus();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsForeground(Window window)
        => GetForegroundWindow() == window.Properties.NativeWindowHandle.Value;

    /// <summary>
    /// Sends one Tab only while FeatherScribe owns the foreground, then polls until
    /// <paramref name="expected"/> has keyboard focus. Returns false (nothing asserted) when the
    /// foreground was lost before or while the key was handled, so the caller restarts the
    /// sequence. Fails with the descriptive message when focus landed elsewhere in FeatherScribe.
    /// </summary>
    private static bool TryTabTo(Window window, AutomationBase automation, AutomationElement expected, string expectation)
    {
        if (!IsForeground(window))
        {
            return false;
        }

        Keyboard.Type(VirtualKeyShort.TAB);
        if (PollUntil(() => expected.Properties.HasKeyboardFocus.ValueOrDefault, FocusPollTimeout))
        {
            return true;
        }

        if (!IsForeground(window))
        {
            return false;
        }

        Assert.Fail($"{expectation}, but focus is on '{DescribeFocus(automation)}'.");
        return false;
    }

    private static bool PollUntil(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (condition())
            {
                return true;
            }

            if (stopwatch.Elapsed >= timeout)
            {
                return false;
            }

            Thread.Sleep(PollInterval);
        }
    }

    private static string DescribeForegroundWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return "(none)";
        }

        var title = new StringBuilder(256);
        GetWindowText(foreground, title, title.Capacity);
        var className = new StringBuilder(256);
        GetClassName(foreground, className, className.Capacity);
        GetWindowThreadProcessId(foreground, out var processId);
        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (Exception)
        {
            processName = "(unknown)";
        }

        return $"title='{title}' class='{className}' process={processName} (pid {processId})";
    }

    private static string DescribeFocus(AutomationBase automation)
    {
        var focused = automation.FocusedElement();
        if (focused is null)
        {
            return "(none)";
        }

        var automationId = focused.Properties.AutomationId.ValueOrDefault;
        var name = focused.Properties.Name.ValueOrDefault;
        return $"{focused.Properties.ControlType.ValueOrDefault} id={automationId} name={name}";
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

    /// <summary>
    /// The in-app feedback snackbar is collapsed until an action reports a result. A collapsed
    /// element may be absent from the UIA tree; if it is present it must be offscreen.
    /// </summary>
    private static void AssertFeedbackNotShown(Window window, string state)
    {
        foreach (var automationId in new[] { "FeedbackHost", "FeedbackText" })
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            Assert.True(
                element is null || element.Properties.IsOffscreen.Value,
                $"[{state}] {automationId} must not be visible before any action result.");
        }
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
            // A real mouse click is input: never send it while another process owns the foreground.
            EnsureForeground(window, window.Automation);
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
    private const int ShowWindowRestore = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

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
