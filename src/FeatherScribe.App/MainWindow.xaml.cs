using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FeatherScribe.Core;

namespace FeatherScribe.App;

/// <summary>
/// 状態表示と直近の結果のコピー/貼り付けを行う最小限のメイン画面。
/// 閉じるボタンはアプリ終了として扱う。
/// </summary>
public partial class MainWindow : Window
{
    private readonly DictationController _controller;
    private readonly ITextOutput _output;
    private readonly ForegroundWindowTracker _foregroundWindowTracker;

    // In-App Feedback hide slide (DIP). Durations and easing come from Themes/Motion.xaml.
    private const double FeedbackHideOffset = 4;

    private readonly InAppFeedbackState _feedbackState = new();

    // The only feedback timer: created once, reused (Stop -> Interval -> Start), stopped in OnClosing.
    private readonly DispatcherTimer _feedbackTimer;

    // Incremented whenever feedback motion starts or stops, so a superseded animation's
    // Completed handler never resets or hides a newer presentation.
    private int _feedbackMotionVersion;

    /// <param name="output">Clipboard output for the explicit copy/repaste actions (no paste guard).</param>
    /// <param name="foregroundWindowTracker">Owned by App; tracks the previous input target.</param>
    public MainWindow(
        DictationController controller,
        AppSettings settings,
        ITextOutput output,
        ForegroundWindowTracker foregroundWindowTracker)
    {
        InitializeComponent();
        _controller = controller;
        _output = output;
        _foregroundWindowTracker = foregroundWindowTracker;
        _feedbackTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _feedbackTimer.Tick += (_, _) => HideFeedback();
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) => FitHeightToContent();
        SizeChanged += (_, _) => KeepInsideWorkArea();
        CandidateExpander.Expanded += (_, _) => FitHeightToContent();
        CandidateExpander.Collapsed += (_, _) => FitHeightToContent();
        OperationGuideExpander.Expanded += (_, _) => FitHeightToContent();
        OperationGuideExpander.Collapsed += (_, _) => FitHeightToContent();

        HotkeyHelpText.Text = UserFacingText.OperationGuide(
            settings.Hotkeys, settings.Llm.Enabled, settings.SelectionEdit.ParsedMode);
    }

    public void UpdateStage(PipelineStage stage, string? message)
    {
        if (UserFacingText.ForStage(stage, _controller.ActiveMode, message) is { } status)
        {
            SetStatus(status);
        }

        RefreshActionAvailability();
    }

    public void UpdateResult(PipelineResult result)
    {
        if (result is { Success: true, Text: not null })
        {
            SetLatestResult(result.Text);
            RejectedResultText.Text = "";
        }

        SetStatus(UserFacingText.ForResult(result));
        RefreshActionAvailability();
    }

    /// <summary>バックグラウンド整形の完了 (成功時は直近の結果を整形結果へ更新)。</summary>
    public void UpdateBackgroundFormatting(BackgroundFormattingResult result)
    {
        if (result.FormattedText is { } formatted)
        {
            SetLatestResult(formatted);
            RejectedResultText.Text = "";
        }
        else if (result.RejectedText is { } rejected)
        {
            SetRejectedResult(rejected);
        }
        else
        {
            // The controller holds no candidate now; do not keep showing one that cannot be adopted.
            RejectedResultText.Text = "";
        }

        SetStatus(UserFacingText.ForBackgroundFormatting(result));
        RefreshActionAvailability();
    }

    /// <summary>ホットキー登録結果を画面に表示する (失敗キーは後からここで確認できる)。</summary>
    /// <param name="actions">Extra action hotkeys (selected-text editing); their failures use the same line format.</param>
    public void ShowHotkeyReport(HotkeyRegistrationReport report, IReadOnlyList<HotkeyActionRegistration>? actions = null)
    {
        var lines = report.Failed.Select(UserFacingText.HotkeyFailureLine)
            .Concat((actions ?? []).Where(action => action.Failed).Select(UserFacingText.HotkeyFailureLine))
            .ToList();
        if (lines.Count == 0)
        {
            return;
        }

        HotkeyHelpText.Text += "\n" + string.Join("\n", lines);
    }

    /// <summary>Which result actions can run now (shared by the buttons and the tray menu).</summary>
    internal ActionAvailability GetActionAvailability() => ActionAvailability.From(_controller);

    // One public entry per action. The button click handlers and the tray menu both call these;
    // each reports its result through In-App Feedback and never throws.

    public async Task CopyLatestAsync()
    {
        try
        {
            if (_controller.LastResult is { } text)
            {
                await _output.OutputAsync(text, OutputMode.ClipboardOnly, CancellationToken.None);
                ShowFeedback(new InAppFeedback(InAppFeedbackKind.Success, InAppFeedbackMessages.CopySucceeded));
            }
        }
        catch (Exception ex)
        {
            // Exception text goes to the debug log only, never to the UI.
            Debug.WriteLine($"[MainWindow] Copy failed: {ex}");
            ShowFeedback(new InAppFeedback(InAppFeedbackKind.Error, InAppFeedbackMessages.CopyFailed));
        }
        finally
        {
            RefreshActionAvailability();
        }
    }

    public async Task PasteLatestToPreviousTargetAsync()
    {
        try
        {
            if (_controller.LastResult is { } text)
            {
                if (!_foregroundWindowTracker.TryRestoreLastExternalWindow())
                {
                    await _output.OutputAsync(text, OutputMode.ClipboardOnly, CancellationToken.None);
                    ShowFeedback(new InAppFeedback(InAppFeedbackKind.Warning, InAppFeedbackMessages.RepasteTargetNotFound));
                    return;
                }

                await _output.OutputAsync(text, OutputMode.ClipboardAndPaste, CancellationToken.None);
                ShowFeedback(new InAppFeedback(InAppFeedbackKind.Success, InAppFeedbackMessages.RepasteSucceeded));
            }
        }
        catch (Exception ex)
        {
            // Exception text goes to the debug log only, never to the UI.
            Debug.WriteLine($"[MainWindow] Repaste failed: {ex}");
            ShowFeedback(new InAppFeedback(InAppFeedbackKind.Error, InAppFeedbackMessages.RepasteFailed));
        }
        finally
        {
            RefreshActionAvailability();
        }
    }

    public void StartReformat()
    {
        try
        {
            if (_controller.ReformatLast() is { } startedMode)
            {
                // Reformatting continues in the background, so StatusText keeps this state text.
                SetStatus(UserFacingText.ForReformatStarted(startedMode));
                ShowFeedback(new InAppFeedback(InAppFeedbackKind.Info, InAppFeedbackMessages.ReformatStarted));
            }
            else
            {
                ShowFeedback(new InAppFeedback(InAppFeedbackKind.Warning, InAppFeedbackMessages.ReformatNotStarted));
            }
        }
        catch (Exception ex)
        {
            // Exception text goes to the debug log only, never to the UI.
            Debug.WriteLine($"[MainWindow] Reformat failed: {ex}");
            ShowFeedback(new InAppFeedback(InAppFeedbackKind.Error, InAppFeedbackMessages.ReformatFailed));
        }
        finally
        {
            RefreshActionAvailability();
        }
    }

    public void AdoptCandidate()
    {
        try
        {
            if (_controller.AdoptRejectedFormattedResult())
            {
                SetLatestResult(_controller.LastResult ?? "");
                RejectedResultText.Text = "";
                SetStatus(UserFacingText.StatusCandidateAdopted);
                ShowFeedback(new InAppFeedback(InAppFeedbackKind.Success, InAppFeedbackMessages.CandidateAdopted));
            }
            else
            {
                ShowFeedback(new InAppFeedback(InAppFeedbackKind.Warning, InAppFeedbackMessages.NoCandidateToAdopt));
            }
        }
        catch (Exception ex)
        {
            // Exception text goes to the debug log only, never to the UI.
            Debug.WriteLine($"[MainWindow] Adopt failed: {ex}");
            ShowFeedback(new InAppFeedback(InAppFeedbackKind.Error, InAppFeedbackMessages.AdoptFailed));
        }
        finally
        {
            RefreshActionAvailability();
        }
    }

    // The entries never throw, so these handlers cannot raise an unhandled exception.
    private async void RecopyButton_Click(object sender, RoutedEventArgs e)
        => await CopyLatestAsync();

    private async void RepasteButton_Click(object sender, RoutedEventArgs e)
        => await PasteLatestToPreviousTargetAsync();

    private void ReformatButton_Click(object sender, RoutedEventArgs e)
        => StartReformat();

    private void AdoptRejectedButton_Click(object sender, RoutedEventArgs e)
        => AdoptCandidate();

    /// <summary>Enables each result action only when it can run now (same meaning as the tray menu).</summary>
    private void RefreshActionAvailability()
    {
        var availability = GetActionAvailability();
        RecopyButton.IsEnabled = availability.CanCopy;
        RepasteButton.IsEnabled = availability.CanRepaste;
        ReformatButton.IsEnabled = availability.CanReformat;
        AdoptRejectedButton.IsEnabled = availability.CanAdopt;
    }

    /// <summary>
    /// Sizes the window height to its content so the page does not scroll, capped by the
    /// current monitor's work area. A manual resize turns SizeToContent off, so it is
    /// re-enabled whenever an expander changes the content height (the width is kept).
    /// The ScrollViewer only scrolls when the content is taller than the work area.
    /// </summary>
    private void FitHeightToContent()
    {
        if (TryGetWorkArea(out var workArea))
        {
            MaxHeight = Math.Max(MinHeight, workArea.Height);
        }

        SizeToContent = SizeToContent.Height;
    }

    private void KeepInsideWorkArea()
    {
        if (WindowState != WindowState.Normal || !TryGetWorkArea(out var workArea))
        {
            return;
        }

        if (Top + ActualHeight > workArea.Bottom)
        {
            Top = Math.Max(workArea.Top, workArea.Bottom - ActualHeight);
        }
    }

    private bool TryGetWorkArea(out Rect workArea)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            workArea = Rect.Empty;
            return false;
        }

        // Screen reports physical pixels; convert to this window's DIPs.
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        workArea = new Rect(
            area.Left / dpi.DpiScaleX,
            area.Top / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX,
            area.Height / dpi.DpiScaleY);
        return true;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        UiMotion.Reveal(MainContentRoot);
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        UiMotion.SubtleUpdate(StatusText);
    }

    /// <summary>
    /// Shows the result of an operation the user just performed. The latest feedback wins:
    /// a visible snackbar is updated in place, a hiding one recovers without re-entering.
    /// Never moves focus or activates the window.
    /// </summary>
    private void ShowFeedback(InAppFeedback feedback)
    {
        var transition = _feedbackState.Show(feedback);

        FeedbackGlyph.Text = feedback.Glyph;
        FeedbackGlyphBackground.Fill = (Brush)FindResource(feedback.GlyphBrushKey);
        FeedbackText.Text = feedback.Message;

        _feedbackTimer.Stop();
        _feedbackTimer.Interval = feedback.Duration;
        _feedbackTimer.Start();

        switch (transition)
        {
            case FeedbackTransition.Enter:
                StopFeedbackMotion();
                UiMotion.Stop(FeedbackText);
                FeedbackHost.Opacity = 0;
                FeedbackTranslate.Y = (double)FindResource("MotionRevealOffset");
                FeedbackHost.Visibility = Visibility.Visible;
                AnimateFeedback(
                    opacity: 1,
                    translateY: 0,
                    GetMotionDuration("MotionNormalDuration"),
                    (IEasingFunction)FindResource("MotionEaseOut"),
                    onCompleted: null);
                break;
            case FeedbackTransition.UpdateInPlace:
                // Content changes immediately; the snackbar itself does not replay its entrance.
                UiMotion.SubtleUpdate(FeedbackText);
                break;
            case FeedbackTransition.RecoverFromHiding:
                // Freeze the hide at the displayed values and return to visible from there.
                StopFeedbackMotion();
                FeedbackHost.Visibility = Visibility.Visible;
                AnimateFeedback(
                    opacity: 1,
                    translateY: 0,
                    GetMotionDuration("MotionFastDuration"),
                    (IEasingFunction)FindResource("MotionEaseOut"),
                    onCompleted: null);
                break;
        }

        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            UIElementAutomationPeer.CreatePeerForElement(FeedbackText)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void HideFeedback()
    {
        _feedbackTimer.Stop();
        if (!_feedbackState.BeginHide())
        {
            return;
        }

        var hideVersion = _feedbackState.Version;
        AnimateFeedback(
            opacity: 0,
            translateY: FeedbackHideOffset,
            GetMotionDuration("MotionNormalDuration"),
            (IEasingFunction)FindResource("MotionEaseInOut"),
            onCompleted: () =>
            {
                // A newer feedback shown during the hide makes this completion stale.
                if (_feedbackState.CompleteHide(hideVersion))
                {
                    FeedbackHost.Visibility = Visibility.Collapsed;
                }
            });
    }

    private void AnimateFeedback(
        double opacity,
        double translateY,
        Duration duration,
        IEasingFunction? easing,
        Action? onCompleted)
    {
        var version = ++_feedbackMotionVersion;

        // No From values: each animation continues from the currently displayed value.
        var opacityAnimation = new DoubleAnimation(opacity, duration) { EasingFunction = easing };
        opacityAnimation.Completed += (_, _) =>
        {
            if (version != _feedbackMotionVersion)
            {
                return;
            }

            SetFeedbackValuesWithoutClocks(opacity, translateY);
            onCompleted?.Invoke();
        };

        FeedbackHost.BeginAnimation(OpacityProperty, opacityAnimation);
        FeedbackTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(translateY, duration) { EasingFunction = easing });
    }

    /// <summary>Cancels any feedback motion (including hide) and freezes it at the displayed values.</summary>
    private void StopFeedbackMotion()
    {
        _feedbackMotionVersion++;
        SetFeedbackValuesWithoutClocks(FeedbackHost.Opacity, FeedbackTranslate.Y);
    }

    private void SetFeedbackValuesWithoutClocks(double opacity, double translateY)
    {
        FeedbackHost.BeginAnimation(OpacityProperty, null);
        FeedbackTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        FeedbackHost.Opacity = opacity;
        FeedbackTranslate.Y = translateY;
    }

    // Motion tokens are app resources (Themes/Motion.xaml); no duration literals here.
    private Duration GetMotionDuration(string key)
        => (Duration)FindResource(key);

    private void SetLatestResult(string text)
    {
        LastResultText.Text = text;
        UiMotion.RevealResult(LastResultText);
    }

    private void SetRejectedResult(string text)
    {
        RejectedResultText.Text = text;
        UiMotion.RevealResult(RejectedResultText);
    }

    /// <summary>トレイの「終了」からメイン画面を閉じる。</summary>
    public void CloseForExit()
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _feedbackTimer.Stop();
        if (!Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Shutdown();
        }
    }
}
