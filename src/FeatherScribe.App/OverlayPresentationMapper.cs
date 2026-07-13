using FeatherScribe.Core;

namespace FeatherScribe.App;

internal static class OverlayPresentationMapper
{
    private static readonly TimeSpan NoDelay = TimeSpan.Zero;
    private static readonly TimeSpan CompletedDelay = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan ReviewDelay = TimeSpan.FromMilliseconds(1800);
    private static readonly TimeSpan FailedDelay = TimeSpan.FromMilliseconds(2200);

    public static OverlayPresentation FromStage(PipelineStage stage)
        => stage switch
        {
            PipelineStage.Recording => Persistent(OverlayVisualState.Recording, "録音中", showsElapsed: true),
            PipelineStage.Transcribing => Persistent(OverlayVisualState.Transcribing, "文字起こし中"),
            PipelineStage.Formatting => Persistent(OverlayVisualState.Formatting, "文章を整えています"),
            PipelineStage.Outputting => Persistent(OverlayVisualState.Pasting, "貼り付け中"),
            PipelineStage.Completed => OverlayPresentation.Hidden,
            PipelineStage.Failed => Temporary(OverlayVisualState.Failed, "処理に失敗しました", FailedDelay),
            _ => OverlayPresentation.Hidden,
        };

    public static OverlayPresentation FromPipelineResult(PipelineResult result)
    {
        if (!result.Success)
        {
            return Temporary(OverlayVisualState.Failed, "処理に失敗しました", FailedDelay);
        }

        if (result.BackgroundFormattingStarted)
        {
            return Persistent(OverlayVisualState.Formatting, "貼り付け完了・整形中");
        }

        if (result.UsedFallback)
        {
            return Temporary(OverlayVisualState.Fallback, "未整形の文章を使用しました", ReviewDelay);
        }

        if (!result.OutputSucceeded)
        {
            return Temporary(OverlayVisualState.Warning, "結果をアプリ内に保持しました", ReviewDelay);
        }

        return Temporary(OverlayVisualState.Completed, "完了", CompletedDelay);
    }

    public static OverlayPresentation FromBackgroundFormattingResult(BackgroundFormattingResult result)
    {
        if (result.FormattedText is not null)
        {
            return Temporary(OverlayVisualState.Completed, "整形完了", CompletedDelay);
        }

        if (result.RejectedText is not null || IsDiscardedFormattingResult(result.ErrorMessage))
        {
            return Temporary(OverlayVisualState.Warning, "整形候補を確認できます", ReviewDelay);
        }

        return Temporary(OverlayVisualState.Fallback, "未整形の文章を使用しました", ReviewDelay);
    }

    private static OverlayPresentation Persistent(
        OverlayVisualState state,
        string text,
        bool showsElapsed = false)
        => new(state, text, showsElapsed, true, NoDelay);

    private static OverlayPresentation Temporary(
        OverlayVisualState state,
        string text,
        TimeSpan autoHideDelay)
        => new(state, text, false, false, autoHideDelay);

    private static bool IsDiscardedFormattingResult(string? errorMessage)
        => errorMessage?.Contains("破棄", StringComparison.Ordinal) == true
            || errorMessage?.Contains("discard", StringComparison.OrdinalIgnoreCase) == true;
}
