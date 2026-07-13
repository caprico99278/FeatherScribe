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

    private readonly DispatcherTimer _recordingTimer;
    private readonly DispatcherTimer _autoHideTimer;

    private DateTimeOffset _recordingStartedAt;
    private OverlayVisualState _currentState = OverlayVisualState.Hidden;

    public RecordingOverlay()
    {
        InitializeComponent();

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

        StopAutoHideTimer();
        StopHideAnimation();
        StopStateAnimations();

        OverlayText.Text = presentation.Text;
        ApplyStateVisuals(presentation.State);
        ApplyElapsedVisibility(presentation);

        if (!IsVisible)
        {
            Show();
        }

        VisualStateManager.GoToElementState(OverlayRoot, presentation.State.ToString(), useTransitions: true);
        PositionAtBottomCenter();
        StartShowAnimation();
        StartStateAnimation(presentation.State);

        if (!presentation.IsPersistent && presentation.AutoHideDelay > TimeSpan.Zero)
        {
            _autoHideTimer.Interval = presentation.AutoHideDelay;
            _autoHideTimer.Start();
        }
    }

    public void HideStatus()
    {
        StopAutoHideTimer();
        StopRecordingTimer();
        StopStateAnimations();

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
        StopHideAnimation();
        base.OnClosed(e);
    }

    private void ApplyStateVisuals(OverlayVisualState state)
    {
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

    private void StartShowAnimation()
    {
        var duration = GetDuration("MotionNormalDuration", TimeSpan.FromMilliseconds(180));
        var easing = TryFindResource("MotionEaseOut") as IEasingFunction;

        OverlayRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
        OverlayTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = easing });
        OverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
        OverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
    }

    private void StartHideAnimation()
    {
        var duration = GetDuration("MotionNormalDuration", TimeSpan.FromMilliseconds(180));
        var easing = TryFindResource("MotionEaseInOut") as IEasingFunction;
        var opacity = new DoubleAnimation(0, duration) { EasingFunction = easing };
        opacity.Completed += (_, _) =>
        {
            if (_currentState == OverlayVisualState.Hidden)
            {
                Hide();
                StopHideAnimation();
            }
        };

        OverlayRoot.BeginAnimation(OpacityProperty, opacity);
        OverlayTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, duration) { EasingFunction = easing });
    }

    private void StopHideAnimation()
    {
        OverlayRoot.BeginAnimation(OpacityProperty, null);
        OverlayTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        OverlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        OverlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

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
        StateGlyph.Visibility = state is OverlayVisualState.Transcribing
            or OverlayVisualState.Formatting
            or OverlayVisualState.Pasting
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void PositionAtBottomCenter()
    {
        var workArea = SystemParameters.WorkArea;
        UpdateLayout();
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Bottom - ActualHeight - 24;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
