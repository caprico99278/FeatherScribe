namespace FeatherScribe.Core;

public interface IAudioRecorder
{
    Task<RecordedAudio> RecordUntilStoppedAsync(
        CancellationToken cancellationToken);
}

public interface ISpeechToTextEngine
{
    Task<TranscriptionResult> TranscribeAsync(
        AudioFile audioFile,
        CancellationToken cancellationToken);
}

public interface ITextFormatter
{
    Task<FormatResult> FormatAsync(
        FormatRequest request,
        CancellationToken cancellationToken);
}

public interface IDictionaryCorrector
{
    string Correct(string text);
}

public interface ITextOutput
{
    Task OutputAsync(
        string text,
        OutputMode mode,
        CancellationToken cancellationToken);
}

public interface IAppSettingsProvider
{
    AppSettings Load();
}

/// <summary>モード別の整形プロンプトテンプレートを提供する。</summary>
public interface IPromptProvider
{
    string GetTemplate(FormattingMode mode);
}

/// <summary>個人辞書のエントリを提供する。壊れたファイルでも例外を出さず空を返すこと。</summary>
public interface IDictionaryProvider
{
    IReadOnlyList<DictionaryEntry> Load();
}

/// <summary>メタデータのみのイベントログ。本文・音声は記録しない。</summary>
public interface IEventLog
{
    void Write(PipelineEvent entry);
}

public sealed record PipelineEvent(
    DateTimeOffset Timestamp,
    string Stage,
    bool Success,
    string? ErrorType,
    long DurationMilliseconds,
    string Mode,
    string? ModelName,
    int CharCount);
