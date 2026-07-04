# 実装指示書: ローカル音声入力アプリ MVP

## 0. 目的

Windowsローカル環境で動作する、Typeless風の音声入力補助アプリを実装する。

本アプリの初期目的は、以下の縦切り処理を安定して実現すること。

```text
ホットキー押下
  ↓
マイク録音
  ↓
whisper.cppで日本語文字起こし
  ↓
Gemma 4で文章整形
  ↓
クリップボードへコピー
  ↓
現在の入力欄へ貼り付け
```

Typeless完全再現は狙わない。
まずは「ローカルで安全に動く音声 → 整形 → 貼り付け」のMVPを完成させる。

---

## 1. 重要方針

### 1.1 優先順位

優先順位は以下。

```text
1. ローカルで動くこと
2. 音声入力から貼り付けまで縦に通ること
3. 失敗時に安全にフォールバックすること
4. Gemma 4が意味を勝手に変えないこと
5. 後からASR/LLM/UIを差し替えられること
```

### 1.2 禁止事項

以下は禁止。

```text
- 最初からTypeless完全再現を狙うこと
- いきなり高度なUIを作ること
- 選択テキスト編集から着手すること
- クラウドAPI前提にすること
- 音声データや全文ログをデフォルト保存すること
- LLMの出力を無検証で貼り付けること
- ASRエンジン、LLMエンジンをアプリ本体へ密結合すること
```

### 1.3 実装スタイル

* OS: Windows
* 言語: C# / .NET
* UI: WPFを第一候補とする
* ASR: whisper.cpp CLI連携
* LLM整形: Gemma 4をOllama API経由で呼び出す
* 代替LLM経路: llama.cpp server
* 出力: クリップボード + Ctrl+V
* テスト: xUnit
* 設定: JSON
* 文字コード: UTF-8
* 改行コード: LF

---

## 2. 最初に行う調査

実装前に、以下を必ず確認すること。

### 2.1 whisper.cpp

確認内容:

```text
- 現在のビルド手順
- Windowsでの推奨ビルド方法
- CLI実行ファイル名
- モデル取得方法
- 日本語指定オプション
- WAV入力時の出力形式
- CPU実行時の速度
```

### 2.2 Gemma 4 + Ollama

確認内容:

```text
- Ollamaで利用可能なGemma 4モデル名
- モデルタグ
- ローカルAPIの呼び出し形式
- /api/chat または /api/generate のどちらを使うべきか
- temperature等の指定方法
- タイムアウト時の挙動
```

### 2.3 llama.cpp server

確認内容:

```text
- OpenAI互換エンドポイントの現在仕様
- /v1/chat/completions の利用可否
- Windowsでの起動方法
- Gemma 4 GGUFモデルの利用可否
```

### 2.4 調査結果の保存

調査結果を以下に保存すること。

```text
docs/research/local_stack_research.md
```

記載項目:

```text
- 確認日
- 参照した公式URL
- 実行したコマンド
- 成功/失敗
- 採用判断
- 未解決リスク
```

---

## 3. 初期ディレクトリ構成

以下の構成で作成すること。

```text
typeless-local/
├─ src/
│  ├─ TypelessLocal.App/
│  ├─ TypelessLocal.Core/
│  ├─ TypelessLocal.Infrastructure/
│  └─ TypelessLocal.Tests/
│
├─ config/
│  ├─ appsettings.json
│  ├─ dictionary.json
│  └─ profiles.json
│
├─ prompts/
│  ├─ plain.md
│  ├─ polite.md
│  ├─ bullet.md
│  ├─ memo.md
│  └─ dev_instruction.md
│
├─ tools/
│  ├─ setup_whisper.ps1
│  ├─ setup_gemma_ollama.ps1
│  ├─ run_asr_test.ps1
│  └─ run_format_test.ps1
│
├─ samples/
│  ├─ audio/
│  ├─ raw/
│  └─ formatted/
│
├─ docs/
│  ├─ architecture.md
│  ├─ roadmap.md
│  ├─ research/
│  │  └─ local_stack_research.md
│  └─ privacy_policy_local.md
│
└─ README.md
```

---

## 4. Phase 0: CLI技術検証

### 4.1 目的

アプリ本体を作る前に、以下が成立することを確認する。

```text
WAV音声
  ↓
whisper.cpp
  ↓
raw transcript
  ↓
Gemma 4
  ↓
formatted text
```

### 4.2 成果物

```text
tools/run_asr_test.ps1
tools/run_format_test.ps1
samples/audio/sample_001.wav
samples/raw/sample_001_raw.txt
samples/formatted/sample_001_formatted.txt
docs/research/local_stack_research.md
```

### 4.3 合格条件

```text
- 30秒程度の日本語音声を文字起こしできる
- Gemma 4で自然な日本語に整形できる
- 整形結果に余計な説明文が混入しない
- ローカルのみで処理が完結する
- 失敗時の原因がログに出る
```

このPhaseが通るまで、WPFアプリ開発に進まないこと。

---

## 5. Phase 1: C#アプリMVP

### 5.1 目的

ホットキー録音からクリップボード貼り付けまでを実装する。

この段階ではGemma 4整形を必須にしない。
まずはwhisper.cpp結果をそのまま貼り付けられることを確認する。

### 5.2 必須機能

```text
- アプリ起動
- グローバルホットキー登録
- ホットキー押下で録音開始
- 再押下で録音停止
- WAVファイル保存
- whisper.cpp CLI呼び出し
- 文字起こし結果取得
- クリップボードへコピー
- Ctrl+V送信
- エラー時は通知またはログ出力
```

### 5.3 推奨コンポーネント

```text
TypelessLocal.Core
  - IAudioRecorder
  - ISpeechToTextEngine
  - ITextFormatter
  - IDictionaryCorrector
  - ITextOutput
  - IAppSettingsProvider

TypelessLocal.Infrastructure
  - NAudioRecorder
  - WhisperCppTranscriptionEngine
  - OllamaGemmaFormatter
  - ClipboardTextOutput
  - JsonAppSettingsProvider

TypelessLocal.App
  - MainWindow
  - TrayIconService
  - HotkeyService
  - RecordingOverlay
```

---

## 6. 中核インターフェース

以下のような責務分離を守ること。

```csharp
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
```

### 6.1 データ型の例

```csharp
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

public enum FormattingMode
{
    NoFormat,
    Plain,
    Polite,
    Bullet,
    Memo,
    DevInstruction
}

public enum OutputMode
{
    ClipboardOnly,
    ClipboardAndPaste
}
```

---

## 7. Phase 2: Gemma 4整形

### 7.1 目的

whisper.cppの生テキストをGemma 4で整形する。

### 7.2 処理順

```text
raw transcript
  ↓
pre dictionary correction
  ↓
Gemma 4 formatting
  ↓
format validation
  ↓
post dictionary correction
  ↓
clipboard output
```

### 7.3 整形プロンプト

`prompts/plain.md` に以下を作成する。

```text
あなたは日本語音声入力の後処理エンジンです。
以下の文字起こし結果を、自然で読みやすい日本語に整形してください。

制約:
- 意味を変えない
- 情報を追加しない
- 話者が言っていない内容を補わない
- 不明な固有名詞を勝手に一般語へ置き換えない
- フィラー語を削除する
- 句読点を補う
- 重複表現を整理する
- 自己修正がある場合は最終意図を採用する
- 出力は整形後テキストのみ
- 解説、前置き、引用符、タイトルは出さない

用語辞書:
{{dictionary}}

文字起こし:
<<<
{{raw_transcript}}
>>>
```

### 7.4 FormatResultValidator

Gemma 4の出力に対して、最低限以下を検証する。

```text
- 空文字ではない
- 「以下が整形結果です」等の前置きがない
- raw transcriptに比べて異常に長すぎない
- エラー文らしき出力ではない
```

異常時は、Gemma 4の結果を破棄し、raw transcriptを使うこと。

### 7.5 合格条件

```text
- Gemma 4が正常時は整形済みテキストを貼り付ける
- Gemma 4失敗時はraw transcriptを貼り付ける
- 失敗してもアプリが落ちない
- 整形に失敗したことがユーザーに分かる
```

---

## 8. Phase 3: 個人辞書

### 8.1 目的

専門用語と固有名詞を安定させる。

### 8.2 dictionary.json

`config/dictionary.json` を作成する。

```json
{
  "entries": [
    {
      "patterns": ["ふぇにっくすくおんつ", "フェニックスクオンツ"],
      "canonical": "PhoenixQuant"
    },
    {
      "patterns": ["こーでっくす", "コードエックス"],
      "canonical": "Codex"
    },
    {
      "patterns": ["うぃすぱー", "ウィスパー"],
      "canonical": "whisper.cpp"
    },
    {
      "patterns": ["じぇま", "ジェマ"],
      "canonical": "Gemma 4"
    }
  ]
}
```

### 8.3 実装方針

```text
- pre-passでraw transcriptを補正
- Gemma 4プロンプトに辞書を注入
- post-passで整形後テキストを再補正
- 辞書ファイルが壊れていてもアプリを落とさない
```

---

## 9. Phase 4: モード切替

### 9.1 実装するモード

```text
NoFormat:
  whisper.cppの結果をほぼそのまま使う

Plain:
  意味を変えず、自然な日本語に整える

Polite:
  丁寧なビジネス文にする

Bullet:
  箇条書きにする

Memo:
  思考メモとして整理する

DevInstruction:
  開発者向けの作業指示に変換する
```

### 9.2 ホットキー

初期案:

```text
Ctrl + Alt + Space:
  Plain

Ctrl + Alt + Shift + Space:
  NoFormat

Ctrl + Alt + M:
  Memo

Ctrl + Alt + D:
  DevInstruction

Ctrl + Alt + B:
  Bullet
```

ホットキー衝突時は設定で変更可能にすること。

---

## 10. Phase 5: 常駐アプリ化

### 10.1 必須機能

```text
- タスクトレイ常駐
- 録音中インジケータ
- 処理中インジケータ
- 設定画面
- 直近結果の再コピー
- 直近結果の再貼り付け
- アプリ終了
```

### 10.2 設定項目

`config/appsettings.json` に以下のような設定を持つ。

```json
{
  "asr": {
    "whisperExecutablePath": "",
    "modelPath": "",
    "language": "ja",
    "threads": 4,
    "timeoutSeconds": 120
  },
  "llm": {
    "provider": "ollama",
    "endpoint": "http://localhost:11434",
    "model": "gemma4",
    "temperature": 0.1,
    "timeoutSeconds": 120
  },
  "output": {
    "mode": "ClipboardAndPaste",
    "pasteDelayMilliseconds": 150,
    "restoreClipboard": false
  },
  "privacy": {
    "saveAudioFiles": false,
    "saveRawTranscript": false,
    "saveFormattedText": false
  }
}
```

モデル名は仮置き。
実装時にOllamaの実際のGemma 4タグを確認して更新すること。

---

## 11. ログとプライバシー

### 11.1 デフォルト方針

デフォルトでは、以下を保存しない。

```text
- 音声ファイル
- 生文字起こし全文
- 整形後テキスト全文
```

### 11.2 保存してよいログ

```text
- 実行日時
- 処理時間
- 成功/失敗
- エラー種別
- 使用モード
- 使用モデル名
- 文字数
```

### 11.3 デバッグモード

デバッグモードをONにした場合のみ、以下を保存可能にする。

```text
- 音声ファイル
- raw transcript
- formatted text
```

ただし、明示的にONにしない限り保存しないこと。

---

## 12. エラーハンドリング

### 12.1 whisper.cpp失敗

```text
- エラーを通知する
- クリップボードは変更しない
- ログにexit codeとstderrを残す
```

### 12.2 Gemma 4失敗

```text
- raw transcriptをfallbackとして使う
- ユーザーに「整形失敗・未整形で貼り付け」と分かる通知を出す
- アプリは落とさない
```

### 12.3 クリップボード失敗

```text
- 結果をアプリ内の直近結果として保持
- ユーザーが再コピーできるようにする
```

### 12.4 貼り付け失敗

```text
- クリップボードには結果を残す
- ユーザーが手動でCtrl+Vできるようにする
```

---

## 13. テスト方針

### 13.1 Unit Test

最低限、以下をテストする。

```text
- DictionaryCorrector
- PromptBuilder
- FormatResultValidator
- SettingsLoader
- WhisperCppCommandBuilder
- OllamaRequestBuilder
```

### 13.2 Integration Test

外部プロセスを使うテストは、明示的なカテゴリに分ける。

```text
- whisper.cpp CLI疎通
- Ollama API疎通
- llama.cpp server疎通
```

通常のCIでは重い統合テストを実行しない。
手元検証用スクリプトとして分離する。

### 13.3 手動受け入れテスト

以下で動作確認する。

```text
- メモ帳
- Chromeの入力欄
- ChatGPT入力欄
- VSCode
- OutlookまたはGmail
```

合格条件:

```text
- 録音できる
- 文字起こしできる
- 整形できる
- クリップボードに入る
- Ctrl+Vで貼り付けられる
- Gemma 4停止中でもraw transcriptでfallbackする
```

---

## 14. 実装順序

必ず以下の順に進める。

```text
1. リポジトリ初期化
2. docs/research/local_stack_research.md 作成
3. whisper.cppの手動検証
4. Gemma 4 + Ollamaの手動検証
5. WAV → raw transcript → formatted text のCLI検証
6. C#ソリューション作成
7. Coreインターフェース作成
8. WhisperCppTranscriptionEngine実装
9. OllamaGemmaFormatter実装
10. ClipboardTextOutput実装
11. ホットキー録音MVP実装
12. 自動貼り付け実装
13. fallback実装
14. 辞書補正実装
15. モード切替実装
16. タスクトレイ常駐化
17. テスト整備
18. README整備
```

順序を崩さないこと。
特に、UI作り込みはPhase 5まで禁止。

---

## 15. 完了条件

MVP完了条件は以下。

```text
- Windows上でアプリを起動できる
- ホットキーで録音開始/停止できる
- whisper.cppで日本語文字起こしできる
- Gemma 4で整形できる
- Gemma 4失敗時にraw transcriptへfallbackできる
- クリップボードへコピーできる
- 現在の入力欄へ貼り付けできる
- 設定ファイルでwhisper.cppパス、モデルパス、Ollama endpoint、モデル名を変更できる
- 音声・全文ログをデフォルト保存しない
- READMEにセットアップ手順がある
```

---

## 16. READMEに必ず書く内容

READMEには以下を記載する。

```text
- アプリの目的
- 必要環境
- whisper.cppのセットアップ
- Gemma 4/Ollamaのセットアップ
- appsettings.jsonの設定方法
- 実行方法
- ホットキー一覧
- トラブルシュート
- プライバシー方針
- 既知の制約
```

---

## 17. 既知の制約として明記すること

```text
- 初期版はWindows専用
- 初期版はクリップボード貼り付け方式
- 選択テキスト編集は未対応
- リアルタイム字幕は未対応
- Gemma 4の整形品質はモデルサイズとPC性能に依存する
- whisper.cppの文字起こし品質はモデルサイズと音声品質に依存する
```

---

## 18. 最終判断基準

この実装の成功条件は「商用Typelessに勝つこと」ではない。

成功条件は以下。

```text
ローカルPC上で、
自分の音声を、
実用的な速度で文字起こしし、
Gemma 4で自然な文章に整形し、
任意の入力欄へ安全に貼り付けられること。
```

この条件を満たす最小実装を最優先で完成させること。
