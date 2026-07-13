namespace FeatherScribe.App;

internal enum OverlayVisualState
{
    Hidden,
    Recording,
    Transcribing,
    Formatting,
    Pasting,
    Completed,
    Fallback,
    Warning,
    Failed,
}

internal sealed record OverlayPresentation(
    OverlayVisualState State,
    string Text,
    bool ShowsElapsed,
    bool IsPersistent,
    TimeSpan AutoHideDelay)
{
    public static OverlayPresentation Hidden { get; } =
        new(OverlayVisualState.Hidden, string.Empty, false, false, TimeSpan.Zero);
}
