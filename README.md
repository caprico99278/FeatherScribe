# FeatherScribe

CyPhoenix用業務向け音声入力アプリ

## アプリの目的

Windowsローカル環境で完結する音声入力補助アプリ。

```text
ホットキー押下 → マイク録音 → whisper.cppで日本語文字起こし
→ (任意) Gemma 4で文章整形 → クリップボードへコピー → 現在の入力欄へ貼り付け
```

音声データ・全文テキストを外部へ送信せず、デフォルトでは保存もしない。
**LLM整形は既定でOFF** (`llm.enabled: false`)。既定動作は「whisper.cppの結果を即貼り付け」の最速構成で、
整形を有効化した場合もrawを先に貼り付け、整形はバックグラウンドで行う (自動置換なし)。

ライセンス: proprietary / all rights reserved ([LICENSE](LICENSE)、
サードパーティは [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md))。

## 必要環境

- Windows 11 (初期版はWindows専用)
- .NET SDK 10.0 以降
- メモリ 16GB 以上推奨 (Gemma 4 e4b をCPU実行する場合)
- ディスク空き 15GB 以上 (whisperモデル + Ollama + Gemma 4)
- マイク

## セットアップ

> ⚠️ **注意: `tools/*.ps1` はPowerShell上での実行が未検証** (開発セッションの実行権限制約のため)。
> スクリプトが行う処理と同等の手順 (公式zipのダウンロード・展開、モデル取得、whisper/Ollama呼び出し) は
> bash/dotnet経由で動作確認済み。初回実行時にエラーが出た場合は
> [docs/research/local_stack_research.md](docs/research/local_stack_research.md) の手動コマンドを参照。

### 1. whisper.cpp

```powershell
powershell -ExecutionPolicy Bypass -File tools/setup_whisper.ps1
```

whisper.cpp v1.9.1 のWindowsバイナリと `ggml-small.bin` を `local/` 配下へ配置する。
モデルサイズは `-ModelSize base|small|medium|large-v3` で変更可能。

### 2. Gemma 4 / Ollama (LLM整形を使う場合のみ)

```powershell
powershell -ExecutionPolicy Bypass -File tools/setup_gemma_ollama.ps1                       # gemma4:e2b (約7.2GB)
powershell -ExecutionPolicy Bypass -File tools/setup_gemma_ollama.ps1 -ModelTag gemma4:e4b  # PlainQuality用 (約9.6GB, 任意)
```

LLM整形はデフォルトOFFのため、whisper.cppのセットアップだけでもアプリは使用できる。

### 3. 動作確認 (Phase 0 検証)

```powershell
powershell -ExecutionPolicy Bypass -File tools/run_asr_test.ps1      # WAV → raw transcript
powershell -ExecutionPolicy Bypass -File tools/run_format_test.ps1   # raw → Gemma 4整形
```

テスト用の日本語WAVが無い場合はTTSで生成できる:

```powershell
dotnet run --project tools/MakeSampleAudio -- samples/audio/sample_001.wav
```

## appsettings.json の設定方法

`config/appsettings.json` で主要な差し替えが可能。

| キー | 説明 |
| --- | --- |
| `asr.whisperExecutablePath` | whisper-cli.exe のパス (リポジトリルート相対可) |
| `asr.modelPath` | ggmlモデルのパス |
| `asr.language` / `asr.threads` / `asr.timeoutSeconds` | 言語 / CPUスレッド数 / タイムアウト |
| `llm.enabled` | LLM整形の有効/無効。**既定 `false`** (全モードが未整形で即貼り付け) |
| `llm.endpoint` | Ollama エンドポイント (既定 `http://localhost:11434`) |
| `llm.model` | 通常モード用の軽量モデル (既定 `gemma4:e2b`) |
| `llm.qualityModel` | PlainQuality専用モデル (既定 `gemma4:e4b`) |
| `llm.timeoutSeconds` | 通常モードのタイムアウト (既定 `8`)。超過時は即raw transcriptを使用 |
| `llm.qualityTimeoutSeconds` | PlainQualityのタイムアウト (既定 `300`) |
| `llm.fallbackToRaw` | 整形失敗時にrawで続行 (既定 `true`) |
| `llm.rawFirstPaste` | PlainFast/PlainQualityでrawを先に貼り付け、整形をバックグラウンド実行 (既定 `true`)。自動置換はしない |
| `llm.temperature` | 生成温度 (既定 `0.1`) |
| `llm.gpuLayers` | GPUオフロード層数。`0`=CPUのみ、削除するとOllama自動判定 |
| `output.mode` | `ClipboardAndPaste` または `ClipboardOnly` |
| `output.pasteDelayMilliseconds` | コピーからCtrl+V送信までの待ち時間 |
| `hotkeys.*` | 各モードのホットキー (例 `"Ctrl+Shift+F9"`) |
| `privacy.*` / `debug.*` | 保存ポリシー (下記「将来拡張用の設定」参照) |

### 将来拡張用・未実装の設定

以下の設定キーは存在するが、現行MVPでは未実装または将来拡張用。

- `config/profiles.json` — 将来のアプリ別プロファイル用。現行MVPでは
  FormattingMode ごとの固定プロンプトマッピング (`FilePromptProvider`) を使用しており、
  このファイルは読み込まれない。
- `privacy.saveRawTranscript` / `privacy.saveFormattedText` / `debug.saveDirectory` —
  raw/整形テキストの全文保存は未実装 (将来のデバッグ用)。現行MVPはデフォルトで
  全文を永続化せず、録音WAV・whisper出力txtも処理後に削除する。
  (`debug.enabled` + `privacy.saveAudioFiles` による録音WAV保持のみ動作する)
- `output.restoreClipboard` — 元クリップボード内容の復元は未実装。
  現行MVPは認識テキストをクリップボードへ書き込み、以前の内容は復元しない。

## 実行方法

```powershell
dotnet run --project src/FeatherScribe.App
```

起動するとメイン画面とタスクトレイアイコンが表示される。
ホットキーを押すと録音開始、もう一度押すと停止し、文字起こし→整形→貼り付けが実行される。
閉じるボタンはトレイへの格納。終了はトレイメニューの「終了」。

## モードとホットキー一覧 (既定)

| キー | モード | 動作 |
| --- | --- | --- |
| `Ctrl+Shift+F8` | NoFormat | whisper.cppの結果を即貼り付け。日常用の最速モード |
| `Ctrl+Shift+F9` | PlainFast | 軽量整形 (gemma4:e2b)。短いタイムアウトで失敗時はraw fallback |
| `Ctrl+Shift+F10` | PlainQuality | 高品質整形 (gemma4:e4b)。待ってもよい時だけ使う |
| `Ctrl+Shift+F11` | Polite | 丁寧なビジネス文にする |
| `Ctrl+Shift+F12` | Bullet | 箇条書きにする |
| `Ctrl+Alt+Shift+M` | Memo | 思考メモとして整理する |
| `Ctrl+Alt+Shift+D` | DevInstruction | Codex等に渡す開発指示書風に整理する |

- 既定値はIME・言語切替と衝突しにくい組み合わせを選んでいる。それでも環境により
  登録に失敗した場合は、**該当キーのみ無効化**して起動を継続し、通知とメイン画面・
  `logs/events.log` で失敗内容 (mode / hotkey / Win32エラー) を確認できる。
- `llm.enabled: false` (既定) の間は、全モードがNoFormat相当 (未整形で即貼り付け) になる。
- PlainFast/PlainQualityは既定で **rawを先に貼り付けた時点で完了**し、整形は
  バックグラウンドで実行される (整形中でも次の録音を開始できる)。整形完了は通知され、
  結果は「再コピー」「再コピー+貼り付け」で利用する (入力欄の自動置換はしない)。
- 変更は `config/appsettings.json` の `hotkeys` で行う。

## トラブルシュート

| 症状 | 対処 |
| --- | --- |
| 「whisper-cli が見つかりません」 | `tools/setup_whisper.ps1` を実行し、`asr.whisperExecutablePath` を確認 |
| 「LLM へ接続できません」 | `ollama serve` が起動しているか、`llm.endpoint` を確認。Gemma 4停止中でも raw transcript で貼り付けは動作する |
| Ollamaが HTTP 500 (out-of-memory) | VRAM不足。`llm.gpuLayers` を `0` にしてCPU実行にする |
| 整形が遅い / タイムアウトする | CPU実行の実測: e2bで約50秒、e4bで約2分強/発話。タイムアウト時は即raw transcriptが使われ「整形失敗・未整形で貼り付け」と通知される。`rawFirstPaste: true` (既定) ならrawが先に貼り付くため待ちは発生しない |
| 貼り付けされない (コピーはされる) | 対象アプリが Ctrl+V を受け付けるか確認。手動 Ctrl+V で回収可能。管理者権限アプリへは通常権限から送信できない |
| ホットキーが効かない | 他アプリと衝突。起動時の通知を確認し `hotkeys` を変更 |
| 失敗の詳細を知りたい | `logs/events.log` (メタデータのみ) を確認 |

## プライバシー方針

- 全処理がローカルで完結。クラウド送信なし
- 音声・生文字起こし・整形後テキストはデフォルト保存しない
- 保存するのはメタデータのみ (日時・処理時間・成否・モード・文字数)
- 詳細: [docs/privacy_policy_local.md](docs/privacy_policy_local.md)

## 既知の制約

- 初期版はWindows専用
- 初期版はクリップボード貼り付け方式 (`Ctrl+V` 送信)
- 選択テキスト編集は未対応
- リアルタイム字幕は未対応
- Gemma 4の整形品質・速度はモデルサイズとPC性能に依存する (本開発機のCPU実行実測: e2b約50秒・e4b約2分強/発話。このためLLM整形は既定OFF)
- whisper.cppの文字起こし品質はモデルサイズと音声品質に依存する
- `tools/*.ps1` はPowerShell上での実行が**未検証** (同等処理はbash/dotnetで検証済み)
- 肉声での手動受け入れテストは**未実施** ([docs/acceptance_test.md](docs/acceptance_test.md) 参照。マイク録音→whisper連携はループバックテストで確認済み)

## 開発

```powershell
dotnet build FeatherScribe.slnx
dotnet test FeatherScribe.slnx --filter "Category!=Integration"   # ユニットテスト
dotnet test FeatherScribe.slnx --filter "Category=Integration"    # whisper/Ollama疎通 (ローカルスタック必須)
```

- 実装指示書: [docs/work/phase1_implementation_instructions_001.md](docs/work/phase1_implementation_instructions_001.md) / [002](docs/work/phase1_implementation_instructions_002.md)
- アーキテクチャ: [docs/architecture.md](docs/architecture.md)
- 調査結果: [docs/research/local_stack_research.md](docs/research/local_stack_research.md)
- 受け入れテスト: [docs/acceptance_test.md](docs/acceptance_test.md)
- ライセンス方針: [docs/licensing.md](docs/licensing.md)
- ロードマップ: [docs/roadmap.md](docs/roadmap.md)
