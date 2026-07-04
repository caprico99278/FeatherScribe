namespace FeatherScribe.Core;

public sealed record AudioFile(
    string Path,
    TimeSpan Duration);

public sealed record RecordedAudio(
    AudioFile File,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

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
    string? ErrorMessage);

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
