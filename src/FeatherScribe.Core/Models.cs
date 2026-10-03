namespace FeatherScribe.Core;

public sealed record AudioFile(
    string Path,
    TimeSpan Duration);

/// <param name="StoppedAtMaxDuration">True when recording.maxRecordingSeconds stopped the recording
/// (not the user's hotkey). The UI tells the user once so speech after the limit is not lost silently.</param>
public sealed record RecordedAudio(
    AudioFile File,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool StoppedAtMaxDuration = false);

/// <summary>The recording of <paramref name="OperationId"/> was stopped by the recording limit.</summary>
/// <param name="LimitSeconds">The effective limit (recording.maxRecordingSeconds, at least 1).</param>
public sealed record RecordingLimitNotice(Guid OperationId, int LimitSeconds);

public sealed record TranscriptionResult(
    string RawText,
    TimeSpan ProcessingTime,
    bool IsSuccess,
    string? ErrorMessage);

public sealed record FormatRequest(
    string RawText,
    FormattingMode Mode,
    IReadOnlyList<DictionaryEntry> DictionaryEntries);

public sealed record FormatResult(
    string Text,
    bool UsedFallback,
    string? ErrorMessage,
    string? RejectedText = null);

public sealed record DictionaryEntry(
    IReadOnlyList<string> Patterns,
    string Canonical);

public enum FormattingMode
{
    /// <summary>whisper.cppの結果を即貼り付けする。日常用の最速モード。</summary>
    NoFormat,

    /// <summary>軽量モデルで整形。短いタイムアウトで失敗時はraw fallback。</summary>
    PlainFast,

    /// <summary>高品質モデル (Gemma 4 E4B等) で整形。待ってもよい時だけ使う。</summary>
    PlainQuality,

    /// <summary>丁寧なビジネス文にする。</summary>
    Polite,

    /// <summary>箇条書きにする。</summary>
    Bullet,

    /// <summary>思考メモとして整理する。</summary>
    Memo,

    /// <summary>Codex等に渡す開発指示書風に整理する。</summary>
    DevInstruction
}

public enum OutputMode
{
    ClipboardOnly,
    ClipboardAndPaste
}
