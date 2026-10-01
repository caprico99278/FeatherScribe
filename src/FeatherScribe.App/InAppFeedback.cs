namespace FeatherScribe.App;

/// <summary>Kind of an in-app operation result shown in the MainWindow snackbar.</summary>
internal enum InAppFeedbackKind
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>
/// The result of an operation the user just performed in MainWindow.
/// Continuous state stays in StatusText; this only carries the short operation result.
/// </summary>
internal sealed record InAppFeedback(InAppFeedbackKind Kind, string Message)
{
    private static readonly TimeSpan SuccessDuration = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan InfoDuration = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan WarningDuration = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromMilliseconds(2200);

    public TimeSpan Duration => DurationFor(Kind);

    public string Glyph => GlyphFor(Kind);

    public string GlyphBrushKey => GlyphBrushKeyFor(Kind);

    public static TimeSpan DurationFor(InAppFeedbackKind kind)
        => kind switch
        {
            InAppFeedbackKind.Success => SuccessDuration,
            InAppFeedbackKind.Info => InfoDuration,
            InAppFeedbackKind.Warning => WarningDuration,
            InAppFeedbackKind.Error => ErrorDuration,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>Glyph shown next to the message, so the kind never depends on color alone.</summary>
    public static string GlyphFor(InAppFeedbackKind kind)
        => kind switch
        {
            InAppFeedbackKind.Success => "✓",
            InAppFeedbackKind.Info => "i",
            InAppFeedbackKind.Warning => "!",
            InAppFeedbackKind.Error => "×",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static string GlyphBrushKeyFor(InAppFeedbackKind kind)
        => kind switch
        {
            InAppFeedbackKind.Success => "SuccessBrush",
            InAppFeedbackKind.Info => "AccentBrush",
            InAppFeedbackKind.Warning => "WarningBrush",
            InAppFeedbackKind.Error => "DangerBrush",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}

/// <summary>Single source for the user-visible in-app feedback messages.</summary>
internal static class InAppFeedbackMessages
{
    public const string CopySucceeded = "クリップボードにコピーしました";
    public const string RepasteSucceeded = "直前の入力先へ貼り付けました";
    public const string RepasteTargetNotFound = "貼り付け先が見つからないため、クリップボードにコピーしました";
    public const string ReformatStarted = "再整形を開始しました";
    public const string ReformatNotStarted = "再整形を開始できませんでした";
    public const string CandidateAdopted = "整形候補を採用しました";
    public const string NoCandidateToAdopt = "採用できる整形候補がありません";
    public const string CopyFailed = "コピーできませんでした";
    public const string RepasteFailed = "貼り付けできませんでした";
    public const string ReformatFailed = "再整形を開始できませんでした";
    public const string AdoptFailed = "整形候補を採用できませんでした";
}

/// <summary>Presentation phase of the in-app feedback snackbar.</summary>
internal enum InAppFeedbackPhase
{
    Hidden,
    Visible,
    Hiding,
}

/// <summary>How the view must present a newly shown feedback.</summary>
internal enum FeedbackTransition
{
    /// <summary>The snackbar was hidden: play the entrance.</summary>
    Enter,

    /// <summary>The snackbar was visible: replace the content in place, no entrance.</summary>
    UpdateInPlace,

    /// <summary>The snackbar was hiding: cancel the hide and return to visible from the current values.</summary>
    RecoverFromHiding,
}

/// <summary>
/// Pure presentation state machine for the in-app feedback snackbar (no WPF, no timers).
/// The latest feedback always wins; there is no queue.
/// </summary>
internal sealed class InAppFeedbackState
{
    public InAppFeedbackPhase Phase { get; private set; } = InAppFeedbackPhase.Hidden;

    public InAppFeedback? Current { get; private set; }

    /// <summary>Incremented on every show and every hide start, so a stale hide completion can be detected.</summary>
    public int Version { get; private set; }

    public FeedbackTransition Show(InAppFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        var transition = Phase switch
        {
            InAppFeedbackPhase.Visible => FeedbackTransition.UpdateInPlace,
            InAppFeedbackPhase.Hiding => FeedbackTransition.RecoverFromHiding,
            _ => FeedbackTransition.Enter,
        };

        Current = feedback;
        Phase = InAppFeedbackPhase.Visible;
        Version++;
        return transition;
    }

    /// <summary>Starts hiding. Only a visible snackbar can start hiding.</summary>
    public bool BeginHide()
    {
        if (Phase != InAppFeedbackPhase.Visible)
        {
            return false;
        }

        Phase = InAppFeedbackPhase.Hiding;
        Version++;
        return true;
    }

    /// <summary>
    /// Completes a hide started at <paramref name="version"/>. A stale completion (a newer show or
    /// hide happened since) is ignored, so it never hides a newer feedback.
    /// </summary>
    public bool CompleteHide(int version)
    {
        if (Phase != InAppFeedbackPhase.Hiding || version != Version)
        {
            return false;
        }

        Phase = InAppFeedbackPhase.Hidden;
        return true;
    }
}
