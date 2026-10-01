using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>What the clipboard held before FeatherScribe wrote its text.</summary>
public enum SnapshotKind
{
    /// <summary>The clipboard was empty; restoring means clearing it.</summary>
    Empty,

    /// <summary>At least one supported format was captured.</summary>
    Captured,

    /// <summary>Nothing reproducible could be captured (unsupported formats only, locked, too large).</summary>
    Unsupported,
}

internal enum RestoreDecision
{
    RestoreNow,
    SkipChanged,
    SkipUnsupported,
}

/// <summary>
/// Pure decision logic for output.restoreClipboard. Clipboard content never passes through here.
/// </summary>
internal static class ClipboardRestorePolicy
{
    /// <summary>Total captured text (all text formats plus file paths) above this is not restored.</summary>
    public const long MaxTextChars = 1_000_000;

    /// <summary>Bitmaps wider or taller than this are not restored.</summary>
    public const int MaxBitmapDimension = 4096;

    /// <summary>
    /// Only an explicit paste overwrites the user's clipboard as a side effect. ClipboardOnly means
    /// the user wants the text on the clipboard (explicit copy, paste-guard fallback), so it is never restored.
    /// </summary>
    public static bool ShouldCapture(bool restoreEnabled, OutputMode mode) =>
        restoreEnabled && mode == OutputMode.ClipboardAndPaste;

    /// <summary>
    /// Decides whether the original clipboard may be written back. The clipboard must still hold exactly
    /// what FeatherScribe set (sequence unchanged); a newer change by the user or another app always wins.
    /// <paramref name="pasteSent"/> does not change the decision, only when it is taken: after the paste
    /// boundary when the paste was sent, immediately (before the error is rethrown) when it was not.
    /// A sequence number of 0 means the clipboard sequence is not accessible, so no guard is possible.
    /// </summary>
    public static RestoreDecision Decide(
        bool pasteSent,
        uint expectedSequence,
        uint currentSequence,
        SnapshotKind snapshotKind)
    {
        _ = pasteSent;

        if (snapshotKind == SnapshotKind.Unsupported || expectedSequence == 0 || currentSequence == 0)
        {
            return RestoreDecision.SkipUnsupported;
        }

        return currentSequence == expectedSequence
            ? RestoreDecision.RestoreNow
            : RestoreDecision.SkipChanged;
    }

    /// <summary>Size guard for the snapshot. A bitmap dimension of 0 means "no bitmap".</summary>
    public static bool IsWithinSizeLimit(long totalTextChars, int bitmapWidth, int bitmapHeight) =>
        totalTextChars <= MaxTextChars &&
        bitmapWidth <= MaxBitmapDimension &&
        bitmapHeight <= MaxBitmapDimension;
}
