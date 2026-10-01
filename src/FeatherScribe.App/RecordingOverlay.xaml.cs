using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;

namespace FeatherScribe.App;

/// <summary>
/// Non-activating status overlay for recording and processing feedback.
/// </summary>
public partial class RecordingOverlay : Window
{
    private const int ExtendedWindowStyle = -20;
    private const int NoActivateStyle = 0x08000000;
    private const int TransparentStyle = 0x00000020;

    // SetWindowPos flags: move only. Never activates, never changes size or Z order (Topmost stays).
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const uint MonitorDefaultToNearest = 0x00000002;

    // Per-bar weights of the volume-linked meter (visual_direction.md §24).
    internal static readonly IReadOnlyList<double> LevelBarWeights = [0.55, 1.0, 0.8, 0.45];
    private const double LevelBarGain = 1.25;

    private readonly DispatcherTimer _recordingTimer;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly AudioLevelSmoother _levelSmoother = new();
    private readonly System.Windows.Shapes.Rectangle[] _levelBars;
    private readonly double _levelBarMinHeight;
    private readonly double _levelBarMaxHeight;
    private readonly ForegroundWindowTracker _foregroundWindowTracker;

    private DateTimeOffset _recordingStartedAt;
    private OverlayVisualState _currentState = OverlayVisualState.Hidden;

    // Incremented whenever shell motion is started or stopped, so a superseded
    // animation's Completed handler can never hide or reset a newer presentation.
    private int _shellMotionVersion;

    // Work area (physical px) of the monitor chosen at the last entrance. Visible state updates
    // keep the overlay anchored here and never re-select the monitor.
    private RectPx? _placementWorkArea;

    /// <param name="foregroundWindowTracker">
    /// App's single tracker (read only here): its last external window picks the overlay monitor
    /// when a FeatherScribe window is in the foreground.
    /// </param>
    public RecordingOverlay(ForegroundWindowTracker foregroundWindowTracker)
    {
        ArgumentNullException.ThrowIfNull(foregroundWindowTracker);
        _foregroundWindowTracker = foregroundWindowTracker;

        InitializeComponent();

        _levelBars = [LevelBar1, LevelBar2, LevelBar3, LevelBar4];
        _levelBarMinHeight = GetDouble("LevelMeterMinBarHeight", 3);
        _levelBarMaxHeight = GetDouble("LevelMeterMaxBarHeight", 14);

        _recordingTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _recordingTimer.Tick += (_, _) => UpdateElapsedText();

        _autoHideTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            HideStatus();
        };
    }

    internal void ShowPresentation(OverlayPresentation presentation)
    {
        if (presentation.State == OverlayVisualState.Hidden)
        {
            HideStatus();
            return;
        }

        var wasVisible = IsVisible;
        StopAutoHideTimer();
        StopShellAnimation();
        StopStateAnimations();

        OverlayText.Text = presentation.Text;
        ApplyStateVisuals(presentation.State);
        ApplyElapsedVisibility(presentation);

        if (!IsVisible)
        {
            PrepareShellForEntrance();
            Show();
        }

        VisualStateManager.GoToElementState(OverlayRoot, presentation.State.ToString(), useTransitions: true);
        if (wasVisible)
        {
            KeepAnchoredOnPlacementMonitor();
            RestoreShellToVisibleState();
        }
        else
        {
            PlaceOnInputTargetMonitor();
            StartShowAnimation();
        }

        StartStateAnimation(presentation.State);

        if (!presentation.IsPersistent && presentation.AutoHideDelay > TimeSpan.Zero)
        {
            _autoHideTimer.Interval = presentation.AutoHideDelay;
            _autoHideTimer.Start();
        }
    }

    /// <summary>
    /// Latest microphone level (0..1) for the volume-linked meter. UI thread only.
    /// Ignored unless the overlay is in Recording; heights are set directly (no timer, no Storyboard).
    /// </summary>
    internal void SetAudioLevel(float level)
    {
        if (_currentState != OverlayVisualState.Recording)
        {
            return;
        }

        var smoothed = _levelSmoother.Next(level);
        for (var i = 0; i < _levelBars.Length; i++)
        {
            _levelBars[i].Height = LevelBarHeight(smoothed, LevelBarWeights[i], _levelBarMinHeight, _levelBarMaxHeight);
        }
    }

    public void HideStatus()
    {
        StopAutoHideTimer();
        StopRecordingTimer();
        StopStateAnimations();
        ResetLevelMeter();
        LevelMeter.Visibility = Visibility.Collapsed;

        if (!IsVisible)
        {
            _currentState = OverlayVisualState.Hidden;
            return;
        }

        _currentState = OverlayVisualState.Hidden;
        StartHideAnimation();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLong(handle, ExtendedWindowStyle);
        _ = SetWindowLong(handle, ExtendedWindowStyle, styles | NoActivateStyle | TransparentStyle);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAutoHideTimer();
        StopRecordingTimer();
        StopStateAnimations();
        ResetLevelMeter();
        StopShellAnimation();
        base.OnClosed(e);
    }

    private void ApplyStateVisuals(OverlayVisualState state)
    {
        // Entering or leaving Recording starts the meter from rest; leaving it stops the meter immediately.
        if (state != OverlayVisualState.Recording || _currentState != OverlayVisualState.Recording)
        {
            ResetLevelMeter();
        }

        _currentState = state;
        IndicatorDot.Fill = GetBrushForState(state);
        StateGlyph.Text = state switch
        {
            OverlayVisualState.Recording => "●",
            OverlayVisualState.Transcribing => "…",
            OverlayVisualState.Formatting => "≋",
            OverlayVisualState.Pasting => "→",
            OverlayVisualState.Completed => "✓",
            OverlayVisualState.Fallback => "△",
            OverlayVisualState.Warning => "!",
            OverlayVisualState.Failed => "×",
            _ => string.Empty,
        };
        ApplyIndicatorVisibility(state);
    }

    private void ApplyElapsedVisibility(OverlayPresentation presentation)
    {
        if (presentation.ShowsElapsed)
        {
            if (_currentState != OverlayVisualState.Recording || !_recordingTimer.IsEnabled)
            {
                _recordingStartedAt = DateTimeOffset.Now;
                UpdateElapsedText();
            }

            ElapsedText.Visibility = Visibility.Visible;
            _recordingTimer.Start();
            return;
        }

        ElapsedText.Visibility = Visibility.Collapsed;
        StopRecordingTimer();
    }

    // Shell motion (OverlayRoot opacity, OverlayTranslate.Y, OverlayScale) has a single owner:
    // AnimateShell starts it, StopShellAnimation freezes it. Entrance, visible-state recovery and
    // hide all go through these two methods so the same properties are never animated twice.

    /// <summary>Entrance: only called when the overlay was not visible.</summary>
    private void StartShowAnimation()
    {
        AnimateShell(
            opacity: 1,
            translateY: 0,
            scale: 1,
            GetDuration("MotionNormalDuration", TimeSpan.FromMilliseconds(180)),
            TryFindResource("MotionEaseOut") as IEasingFunction,
            onCompleted: null);
    }

    private void PrepareShellForEntrance()
    {
        OverlayRoot.Opacity = 0;
        OverlayTranslate.Y = GetDouble("MotionRevealOffset", 6);
        OverlayScale.ScaleX = 0.97;
        OverlayScale.ScaleY = 0.97;
    }

    /// <summary>
    /// Visible state update. A fully visible shell is left untouched (no re-entrance);
    /// a shell caught mid-hide continues from its current values back to visible.
    /// </summary>
    private void RestoreShellToVisibleState()
    {
        if (IsShellAtRest())
        {
            return;
        }

        AnimateShell(
            opacity: 1,
            translateY: 0,
            scale: 1,
            GetDuration("MotionFastDuration", TimeSpan.FromMilliseconds(100)),
            TryFindResource("MotionEaseOut") as IEasingFunction,
            onCompleted: null);
    }

    private void StartHideAnimation()
    {
        AnimateShell(
            opacity: 0,
            translateY: GetDouble("MotionRevealOffset", 6),
            scale: OverlayScale.ScaleX,
            GetDuration("MotionNormalDuration", TimeSpan.FromMilliseconds(180)),
            TryFindResource("MotionEaseInOut") as IEasingFunction,
            onCompleted: () =>
            {
                if (_currentState == OverlayVisualState.Hidden)
                {
                    Hide();
                }
            });
    }

    private void AnimateShell(
        double opacity,
        double translateY,
        double scale,
        Duration duration,
        IEasingFunction? easing,
        Action? onCompleted)
    {
        var version = ++_shellMotionVersion;

        // No From values: each animation continues from the currently displayed value.
        var opacityAnimation = new DoubleAnimation(opacity, duration) { EasingFunction = easing };
        opacityAnimation.Completed += (_, _) =>
        {
            if (version != _shellMotionVersion)
            {
                return;
            }

            SetShellValuesWithoutClocks(opacity, translateY, scale, scale);
            onCompleted?.Invoke();
        };

        OverlayRoot.BeginAnimation(OpacityProperty, opacityAnimation);
        OverlayTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateY, duration) { EasingFunction = easing });
        OverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        OverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
    }

    /// <summary>Cancels any shell motion (including hide) and freezes the shell at its displayed values.</summary>
    private void StopShellAnimation()
    {
        _shellMotionVersion++;
        SetShellValuesWithoutClocks(
            OverlayRoot.Opacity,
            OverlayTranslate.Y,
            OverlayScale.ScaleX,
            OverlayScale.ScaleY);
    }

    private void SetShellValuesWithoutClocks(double opacity, double translateY, double scaleX, double scaleY)
    {
        OverlayRoot.BeginAnimation(OpacityProperty, null);
        OverlayTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        OverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        OverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        OverlayRoot.Opacity = opacity;
        OverlayTranslate.Y = translateY;
        OverlayScale.ScaleX = scaleX;
        OverlayScale.ScaleY = scaleY;
    }

    private bool IsShellAtRest()
        => IsClose(OverlayRoot.Opacity, 1)
            && IsClose(OverlayTranslate.Y, 0)
            && IsClose(OverlayScale.ScaleX, 1)
            && IsClose(OverlayScale.ScaleY, 1);

    private static bool IsClose(double actual, double expected)
        => Math.Abs(actual - expected) < 0.001;

    private void StartStateAnimation(OverlayVisualState state)
    {
        switch (state)
        {
            case OverlayVisualState.Recording:
                IndicatorDot.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(650))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                    });
                break;
            case OverlayVisualState.Transcribing:
                StartDotAnimation(TranscribingDot1, TimeSpan.Zero);
                StartDotAnimation(TranscribingDot2, TimeSpan.FromMilliseconds(160));
                StartDotAnimation(TranscribingDot3, TimeSpan.FromMilliseconds(320));
                break;
            case OverlayVisualState.Formatting:
                FormattingSweepTranslate.BeginAnimation(
                    TranslateTransform.XProperty,
                    new DoubleAnimation(0, 9, TimeSpan.FromMilliseconds(720))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                    });
                break;
            case OverlayVisualState.Pasting:
                PastingArrowTranslate.BeginAnimation(
                    TranslateTransform.XProperty,
                    new DoubleAnimation(-2, 3, TimeSpan.FromMilliseconds(420))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                    });
                break;
        }
    }

    private void StopStateAnimations()
    {
        IndicatorDot.BeginAnimation(OpacityProperty, null);
        StateGlyph.BeginAnimation(OpacityProperty, null);
        TranscribingDot1.BeginAnimation(OpacityProperty, null);
        TranscribingDot2.BeginAnimation(OpacityProperty, null);
        TranscribingDot3.BeginAnimation(OpacityProperty, null);
        FormattingSweepTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PastingArrowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        IndicatorDot.Opacity = 0.85;
        StateGlyph.Opacity = 1;
        TranscribingDot1.Opacity = 0.35;
        TranscribingDot2.Opacity = 0.35;
        TranscribingDot3.Opacity = 0.35;
        FormattingSweepTranslate.X = 0;
        PastingArrowTranslate.X = -2;
        TranscribingDots.Visibility = Visibility.Collapsed;
        FormattingSweep.Visibility = Visibility.Collapsed;
        PastingArrow.Visibility = Visibility.Collapsed;
        StateGlyph.Visibility = Visibility.Visible;
    }

    private static void StartDotAnimation(UIElement dot, TimeSpan beginTime)
    {
        dot.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(360))
            {
                AutoReverse = true,
                BeginTime = beginTime,
                RepeatBehavior = RepeatBehavior.Forever,
            });
    }

    private void ApplyIndicatorVisibility(OverlayVisualState state)
    {
        TranscribingDots.Visibility = state == OverlayVisualState.Transcribing
            ? Visibility.Visible
            : Visibility.Collapsed;
        FormattingSweep.Visibility = state == OverlayVisualState.Formatting
            ? Visibility.Visible
            : Visibility.Collapsed;
        PastingArrow.Visibility = state == OverlayVisualState.Pasting
            ? Visibility.Visible
            : Visibility.Collapsed;
        LevelMeter.Visibility = state == OverlayVisualState.Recording
            ? Visibility.Visible
            : Visibility.Collapsed;
        StateGlyph.Visibility = state is OverlayVisualState.Transcribing
            or OverlayVisualState.Formatting
            or OverlayVisualState.Pasting
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    /// <summary>height = min + (max - min) * clamp(smoothed * weight * 1.25, 0, 1).</summary>
    internal static double LevelBarHeight(double smoothed, double weight, double minHeight, double maxHeight)
        => minHeight + (maxHeight - minHeight) * Math.Clamp(smoothed * weight * LevelBarGain, 0, 1);

    private void ResetLevelMeter()
    {
        _levelSmoother.Reset();
        foreach (var bar in _levelBars)
        {
            bar.Height = _levelBarMinHeight;
        }
    }

    // Placement (visual_direction.md section 25). Entrance only: pick the monitor of the input
    // target and move to its bottom center. The window is moved with SetWindowPos(SWP_NOACTIVATE);
    // it is never activated and the foreground window is never changed.

    /// <summary>Entrance: select the monitor, then place twice so a DPI change on the move is corrected.</summary>
    private void PlaceOnInputTargetMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !TryGetInputTargetWorkArea(out var workArea))
        {
            _placementWorkArea = null;
            return;
        }

        _placementWorkArea = workArea;

        // Pass 1 uses the current size and DPI. If the target monitor has another DPI, the move
        // makes WPF apply that DPI and resize the window (WM_DPICHANGED).
        MoveToBottomCenter(handle, workArea);

        // Pass 2 re-measures at the target monitor's DPI and corrects the position. It does not
        // move the window when nothing changed.
        MoveToBottomCenter(handle, workArea);
    }

    /// <summary>
    /// Visible state update: the monitor is not re-selected. Only when the pill's size changed
    /// (different text) is it re-centered on the same bottom-center anchor, so it never jumps.
    /// </summary>
    private void KeepAnchoredOnPlacementMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && _placementWorkArea is { } workArea)
        {
            MoveToBottomCenter(handle, workArea);
        }
    }

    private void MoveToBottomCenter(IntPtr handle, RectPx workArea)
    {
        UpdateLayout();
        if (!GetWindowRect(handle, out var bounds))
        {
            return;
        }

        var current = new RectPx(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var marginPx = OverlayPlacement.DipToPx(OverlayPlacement.BottomMarginDip, scale);

        // OverlayRoot's uniform margin (OverlayShadowInset) is transparent room for the shadow: the
        // placement works on the visible pill, so its bottom stays 24 DIP above the work area.
        var insetPx = OverlayPlacement.DipToPx(OverlayRoot.Margin.Bottom, scale);
        var (x, y) = OverlayPlacement.BottomCenter(workArea, current.Width, current.Height, marginPx, insetPx);
        if (x == current.Left && y == current.Top)
        {
            return;
        }

        _ = SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    /// <summary>
    /// Work area (physical px, taskbar excluded) of: the foreground window when it is not ours,
    /// else the tracker's last external window, else the monitor under the cursor, else the primary.
    /// </summary>
    private bool TryGetInputTargetWorkArea(out RectPx workArea)
    {
        workArea = default;

        var foreground = GetForegroundWindow();
        var foregroundIsOwnProcess = false;
        if (foreground != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(foreground, out var processId);
            foregroundIsOwnProcess = processId == Environment.ProcessId;
        }

        var target = OverlayPlacement.SelectTargetWindow(
            foreground,
            foregroundIsOwnProcess,
            _foregroundWindowTracker.LastExternalWindow);

        var monitor = IntPtr.Zero;
        if (target != IntPtr.Zero && IsWindow(target))
        {
            monitor = MonitorFromWindow(target, MonitorDefaultToNearest);
        }

        if (monitor == IntPtr.Zero && GetCursorPos(out var cursor))
        {
            monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        }

        if (monitor == IntPtr.Zero)
        {
            monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        }

        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        workArea = new RectPx(info.WorkArea.Left, info.WorkArea.Top, info.WorkArea.Right, info.WorkArea.Bottom);
        return workArea.Width > 0 && workArea.Height > 0;
    }

    private void UpdateElapsedText()
    {
        var elapsed = DateTimeOffset.Now - _recordingStartedAt;
        ElapsedText.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    private void StopRecordingTimer()
    {
        _recordingTimer.Stop();
        ElapsedText.Text = "00:00";
    }

    private void StopAutoHideTimer()
    {
        _autoHideTimer.Stop();
    }

    private Brush GetBrushForState(OverlayVisualState state)
    {
        var key = state switch
        {
            OverlayVisualState.Recording => "RecordingBrush",
            OverlayVisualState.Completed => "SuccessBrush",
            OverlayVisualState.Fallback or OverlayVisualState.Warning => "WarningBrush",
            OverlayVisualState.Failed => "DangerBrush",
            _ => "AccentBrush",
        };

        return (TryFindResource(key) as Brush) ?? Brushes.White;
    }

    private Duration GetDuration(string key, TimeSpan fallback)
        => TryFindResource(key) is Duration duration ? duration : new Duration(fallback);

    private double GetDouble(string key, double fallback)
        => TryFindResource(key) is double value ? value : fallback;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
