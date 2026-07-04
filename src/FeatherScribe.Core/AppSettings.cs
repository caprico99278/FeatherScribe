namespace FeatherScribe.Core;

public sealed class AppSettings
{
    public AsrSettings Asr { get; init; } = new();
    public LlmSettings Llm { get; init; } = new();
    public RecordingSettings Recording { get; init; } = new();
    public OutputSettings Output { get; init; } = new();
    public HotkeySettings Hotkeys { get; init; } = new();
    public PrivacySettings Privacy { get; init; } = new();
    public DebugSettings Debug { get; init; } = new();
}

public sealed class AsrSettings
{
    public string WhisperExecutablePath { get; init; } = "";
    public string ModelPath { get; init; } = "";
    public string Language { get; init; } = "ja";
    public int Threads { get; init; } = 4;
    public int TimeoutSeconds { get; init; } = 120;
}

public sealed class LlmSettings
{
    /// <summary>LLM整形の有効/無効。既定OFF (初期UXを最速のraw貼り付けに保つ)。</summary>
    public bool Enabled { get; init; } = false;

    public string Provider { get; init; } = "ollama";
    public string Endpoint { get; init; } = "http://localhost:11434";

    /// <summary>通常モード (PlainFast等) 用の軽量モデル。</summary>
    public string Model { get; init; } = "gemma4:e2b";

    /// <summary>PlainQuality専用の高品質モデル。待ってもよい時だけ使う。</summary>
    public string QualityModel { get; init; } = "gemma4:e4b";

    public double Temperature { get; init; } = 0.1;

    /// <summary>通常モードのタイムアウト。超過時は即raw transcriptへフォールバックする。</summary>
    public int TimeoutSeconds { get; init; } = 8;

    /// <summary>PlainQualityモードのタイムアウト。</summary>
    public int QualityTimeoutSeconds { get; init; } = 300;

    /// <summary>整形失敗・タイムアウト時にraw transcriptで続行する (falseなら出力せずエラー扱い)。</summary>
    public bool FallbackToRaw { get; init; } = true;

    /// <summary>
    /// PlainFast/PlainQualityで、raw transcriptを先に貼り付けてから整形をバックグラウンド実行する。
    /// 整形結果の自動置換はせず、ユーザー操作 (再コピー/再貼り付け) でのみ利用可能にする。
    /// </summary>
    public bool RawFirstPaste { get; init; } = true;

    /// <summary>
    /// GPUへオフロードするレイヤ数 (Ollama options.num_gpu)。
    /// null: Ollama自動判定 / 0: CPUのみ (VRAM不足でロードに失敗する環境向け)。
    /// </summary>
    public int? GpuLayers { get; init; }
}

public sealed class RecordingSettings
{
    public int SampleRate { get; init; } = 16000;
    public int Channels { get; init; } = 1;
    public int MaxRecordingSeconds { get; init; } = 300;
}

public sealed class OutputSettings
{
    public string Mode { get; init; } = "ClipboardAndPaste";
    public int PasteDelayMilliseconds { get; init; } = 150;
    public bool RestoreClipboard { get; init; } = false;

    public OutputMode ParsedMode =>
        Enum.TryParse<OutputMode>(Mode, ignoreCase: true, out var parsed)
            ? parsed
            : OutputMode.ClipboardAndPaste;
}

// 既定値はIME・言語切替等と衝突しにくい組み合わせにする (指示書002 §2)。
// それでも環境により登録失敗しうるため、失敗時はそのキーのみ無効化して起動を継続する。
public sealed class HotkeySettings
{
    public string NoFormat { get; init; } = "Ctrl+Shift+F8";
    public string PlainFast { get; init; } = "Ctrl+Shift+F9";
    public string PlainQuality { get; init; } = "Ctrl+Shift+F10";
    public string Polite { get; init; } = "Ctrl+Shift+F11";
    public string Bullet { get; init; } = "Ctrl+Shift+F12";
    public string Memo { get; init; } = "Ctrl+Alt+Shift+M";
    public string DevInstruction { get; init; } = "Ctrl+Alt+Shift+D";
}

public sealed class PrivacySettings
{
    public bool SaveAudioFiles { get; init; } = false;
    public bool SaveRawTranscript { get; init; } = false;
    public bool SaveFormattedText { get; init; } = false;
}

public sealed class DebugSettings
{
    public bool Enabled { get; init; } = false;
    public string SaveDirectory { get; init; } = "debug_artifacts";

    /// <summary>デバッグモードがONのときのみ本文系の保存を許可する(指示書§11.3)。</summary>
    public bool AllowContentSaving => Enabled;
}
