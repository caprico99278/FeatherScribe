namespace FeatherScribe.Infrastructure;

/// <summary>
/// Clipboard operations used by <see cref="ClipboardTextOutput"/> and selected-text editing. The production
/// implementation (<see cref="WpfClipboardAccess"/>) touches the real clipboard on STA threads; unit tests use a fake.
/// Public so the App-side selection edit (and a runtime probe) can use the same access and sequence guard.
/// </summary>
public interface IClipboardAccess
{
    /// <summary>
    /// Captures the supported formats of the current clipboard into memory. Never throws: a clipboard
    /// that cannot be reproduced (unsupported formats only, locked, too large) yields
    /// <see cref="SnapshotKind.Unsupported"/>.
    /// </summary>
    Task<ClipboardSnapshot> CaptureSnapshotAsync();

    /// <summary>Sets the text (existing behavior, used when restoreClipboard is off).</summary>
    Task SetTextAsync(string text);

    /// <summary>Sets the text and returns the clipboard sequence number read right after the write.</summary>
    Task<uint> SetTextAndGetSequenceAsync(string text);

    /// <summary>Current clipboard sequence number (0 when not accessible).</summary>
    uint GetSequenceNumber();

    /// <summary>
    /// Writes the snapshot back (or clears the clipboard for an empty snapshot), but only while the clipboard
    /// sequence number still equals <paramref name="expectedSequence"/>; the check is repeated right before
    /// every write attempt. Returns false when the clipboard changed and nothing was written.
    /// </summary>
    Task<bool> RestoreAsync(ClipboardSnapshot snapshot, uint expectedSequence);

    /// <summary>
    /// Reads the copied selection in one STA call: the Unicode text and whether the source app marked the copy
    /// as coming from an empty selection (VSCode copies the whole line then, see <see cref="VsCodeClipboardMarker"/>).
    /// May throw when the clipboard cannot be opened; the caller maps that to its abort flow.
    /// </summary>
    Task<CapturedSelection> ReadSelectionAsync();
}

/// <summary>Text read from the clipboard right after a copy. Never logged or persisted.</summary>
/// <param name="Text">Unicode text, or null when the clipboard holds no text.</param>
/// <param name="IsFromEmptySelection">True when the copy came from an empty selection (VSCode line copy).</param>
public sealed record CapturedSelection(string? Text, bool IsFromEmptySelection);

/// <summary>
/// In-memory copy of the user's clipboard, held only for the duration of one paste and never written anywhere.
/// </summary>
public sealed class ClipboardSnapshot
{
    public const string ReasonUnsupportedFormats = "unsupported_formats";
    public const string ReasonTooLarge = "too_large";
    public const string ReasonCaptureFailed = "capture_failed";

    private ClipboardSnapshot(SnapshotKind kind, int formatCount, string? unsupportedReason, object? payload)
    {
        Kind = kind;
        FormatCount = formatCount;
        UnsupportedReason = unsupportedReason;
        Payload = payload;
    }

    public static ClipboardSnapshot Empty { get; } = new(SnapshotKind.Empty, 0, null, null);

    public SnapshotKind Kind { get; }

    /// <summary>Number of captured formats (metadata only, safe to log).</summary>
    public int FormatCount { get; }

    /// <summary>Why the snapshot is <see cref="SnapshotKind.Unsupported"/> (metadata only, safe to log).</summary>
    public string? UnsupportedReason { get; }

    /// <summary>Implementation-specific captured data (a WPF data object in production). Never logged.</summary>
    public object? Payload { get; }

    public static ClipboardSnapshot Captured(object payload, int formatCount)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(formatCount);
        return new ClipboardSnapshot(SnapshotKind.Captured, formatCount, null, payload);
    }

    public static ClipboardSnapshot Unsupported(string reason) =>
        new(SnapshotKind.Unsupported, 0, reason, null);
}
