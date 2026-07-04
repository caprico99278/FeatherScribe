# アーキテクチャ

## 処理フロー

```text
ホットキー押下 (HotkeyService)
  ↓
録音開始 (NAudioRecorder / 16kHz 16bit mono WAV)
  ↓ (再押下で停止)
whisper.cpp CLI (WhisperCppTranscriptionEngine)
  ↓ raw transcript
辞書補正 pre-pass (DictionaryCorrector)
  ↓
Gemma 4整形 (OllamaGemmaFormatter → /api/chat)
  ↓ 検証 (FormatResultValidator) — 不合格ならrawへフォールバック
辞書補正 post-pass (DictionaryCorrector)
  ↓
クリップボード + Ctrl+V (ClipboardTextOutput)
```

オーケストレーションは `FeatherScribe.Core.DictationPipeline` が担い、
各段の失敗は指示書§12のフォールバック方針に従う。

補足 (初期UX方針):

- `llm.enabled: false` (既定) の間はGemma整形をスキップし、辞書補正のみで即貼り付けする
- PlainFast/PlainQualityでは `llm.rawFirstPaste: true` (既定) により **rawを先に貼り付け**、
  整形はバックグラウンドで実行する。整形結果は再コピー/再貼り付け用に保持され、
  **入力欄の自動置換は行わない**
- PlainQualityのみ `llm.qualityModel` (E4B) + `llm.qualityTimeoutSeconds` を使用し、
  他モードは軽量モデル + 短タイムアウト (超過時は即raw)

## プロジェクト構成と責務

```text
FeatherScribe.Core           依存なし。ドメイン型・インターフェース・純粋ロジック
  ├─ IAudioRecorder / ISpeechToTextEngine / ITextFormatter
  │  IDictionaryCorrector / ITextOutput / IAppSettingsProvider
  ├─ DictationPipeline       縦のオーケストレーションとフォールバック
  ├─ DictionaryCorrector     辞書置換 (長いパターン優先)
  ├─ PromptBuilder           {{dictionary}} / {{raw_transcript}} の展開
  ├─ FormatResultValidator   LLM出力の検証 (空・前置き・過長)
  └─ HotkeyParser            "Ctrl+Alt+Space" → RegisterHotKey 値

FeatherScribe.Infrastructure 外部技術との接続 (差し替え点)
  ├─ NAudioRecorder                 NAudio WaveInEvent
  ├─ WhisperCppTranscriptionEngine  whisper-cli.exe プロセス起動
  ├─ WhisperCppCommandBuilder       CLI引数の組み立て (テスト可能)
  ├─ OllamaGemmaFormatter           Ollama /api/chat クライアント
  ├─ OllamaRequestBuilder           リクエストJSON生成/解析 (テスト可能)
  ├─ ClipboardTextOutput            クリップボード + SendInputでCtrl+V
  ├─ JsonAppSettingsProvider        config/appsettings.json (壊れていても既定値で起動)
  ├─ JsonDictionaryProvider         config/dictionary.json (壊れていても空辞書)
  ├─ FilePromptProvider             prompts/*.md (無ければ組み込み既定)
  │                                 ※ FormattingModeごとの固定マッピング。
  │                                   config/profiles.json は将来のアプリ別プロファイル用で、現行MVPでは未読込
  └─ FileEventLog                   メタデータのみのJSON Linesログ

FeatherScribe.App            WPF常駐アプリ
  ├─ App                     コンポジションルート
  ├─ DictationController     Idle→Recording→Processing の状態機械
  ├─ HotkeyService           RegisterHotKey / WM_HOTKEY
  ├─ TrayIconService         トレイ常駐・通知・再コピー/再貼り付け・終了
  ├─ MainWindow              状態表示と直近結果
  └─ RecordingOverlay        録音中/処理中インジケータ
```

## エンジン差し替え方針

ASR / LLM / 出力はすべて Core のインターフェース越しに使用しており、
アプリ本体 (App) は具象型を `App.xaml.cs` のコンポジションルートでのみ参照する。

- ASRを差し替える → `ISpeechToTextEngine` 実装を追加
- LLMを llama.cpp server (OpenAI互換) へ → `ITextFormatter` 実装を追加し
  `llm.provider` 設定で切り替え
- UIを差し替える → Core/Infrastructureはそのまま利用可能

## フォールバック設計 (指示書§12)

| 失敗箇所 | 挙動 |
| --- | --- |
| whisper.cpp | 通知。クリップボードは変更しない。exit code / stderr をログ |
| Gemma 4 | raw transcript で続行 (`UsedFallback=true`)。「整形失敗・未整形で貼り付け」通知 |
| クリップボード | 直近結果をアプリ内に保持。メイン画面/トレイから再コピー |
| Ctrl+V送信 | クリップボードには残る。手動Ctrl+Vで回収可能 |

## プライバシー (指示書§11)

- 録音WAVは一時ディレクトリに書き、処理後に削除
- whisper.cppの出力txtも読み取り後に削除
- `logs/events.log` には日時・処理時間・成否・エラー種別・モード・モデル名・文字数のみ
- `debug.enabled=true` かつ `privacy.save*=true` の場合のみ本文系の保存を許可
