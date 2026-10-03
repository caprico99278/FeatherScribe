# FeatherScribe

CyPhoenix用業務向け音声入力アプリ

## アプリの目的

Windowsローカル環境で完結する音声入力補助アプリ。

```text
ホットキー押下 → マイク録音 → whisper.cppで日本語文字起こし
→ (任意) Gemma 4で文章整形 → クリップボードへコピー → 直前の入力先へ貼り付け
```

音声データ・全文テキストを外部へ送信せず、デフォルトでは保存もしない。
**LLM整形は既定でOFF** (`llm.enabled: false`)。既定動作は「whisper.cppの結果を即貼り付け」の最速構成で、
整形を有効化した場合もrawを先に貼り付け、整形はバックグラウンドで行う (自動置換なし)。

UIデザイン方針は [docs/ui/visual_direction.md](docs/ui/visual_direction.md) を参照。

ライセンス: MIT ([LICENSE](LICENSE)、
サードパーティは [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md))。

## 必要環境

- Windows 11 (初期版はWindows専用)
- .NET SDK 10.0 以降
- メモリ 16GB 以上推奨 (Gemma 4 e4b をCPU実行する場合)
- ディスク空き 15GB 以上 (whisperモデル + Ollama + Gemma 4)
- マイク

## セットアップ

> `tools/*.ps1` は Windows PowerShell 5.1 で実行確認済み (2026-10-01。セットアップ系はローカルスタック取得済みの状態で、
> ダウンロードを伴わない経路を確認)。初回のダウンロードでエラーが出た場合は
> [docs/research/local_stack_research.md](docs/research/local_stack_research.md) の手動コマンドを参照。

### 1. whisper.cpp

```powershell
powershell -ExecutionPolicy Bypass -File tools/setup_whisper.ps1
```

whisper.cpp v1.9.1 のWindowsバイナリと `ggml-small.bin` を `local/` 配下へ配置する。
モデルサイズは `-ModelSize base|small|medium|large-v3|large-v3-turbo|large-v3-turbo-q5_0` で変更可能。

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
標準設定は `config/appsettings.json` に置き、個人検証用の上書き設定は `config/appsettings.local.json` に置く。
`appsettings.local.json` は Git 管理外で、存在する場合だけ起動時に追加読み込みされる。
local 側は部分設定を許可しており、書いたプロパティだけが base の `appsettings.json` を上書きする。
TinySwallow などの実験用 LLM モデル設定は `appsettings.json` ではなく `appsettings.local.json` に書く。

例:

```json
{
  "llm": {
    "enabled": true,
    "model": "hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M",
    "timeoutSeconds": 20,
    "numPredict": 80,
    "numContext": 1024,
    "keepAlive": "30m"
  }
}
```

| キー | 説明 |
| --- | --- |
| `asr.whisperExecutablePath` | whisper-cli.exe のパス (リポジトリルート相対可) |
| `asr.modelPath` | ggmlモデルのパス |
| `asr.language` / `asr.threads` | 言語 / CPUスレッド数 |
| `asr.timeoutSeconds` | 最小タイムアウト（実際は録音の長さに応じて延長：録音秒×3+30秒） (既定 `120`)。例: 5分の録音は930秒 |
| `recording.maxRecordingSeconds` | 1回の録音の上限秒数 (既定 `300` = 5分)。上限に達すると録音を自動停止し、オーバーレイとトレイ通知で知らせる |
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
| `output.restoreClipboard` | 貼り付け後に元のクリップボード内容を復元 (既定 `false`)。下記「クリップボードの復元」参照 |
| `hotkeys.*` | 各モードのホットキー (例 `"Ctrl+Shift+F9"`) |
| `hotkeys.editSelection` | 選択テキスト編集のホットキー (既定 `"Ctrl+Shift+F7"`)。`""` で無効 |
| `selectionEdit.mode` | 選択テキスト編集で使う整形モード (既定 `"Polite"`)。`NoFormat` や不正値は `Polite` に戻し、起動時に警告する |
| `selectionEdit.captureTimeoutMilliseconds` | 選択テキストのコピーを待つ時間 (既定 `600`、100〜5000) |
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

### クリップボードの復元 (`output.restoreClipboard`)

`true` にすると、自動貼り付け (`ClipboardAndPaste`) の後に元のクリップボード内容を戻す。
既定は `false` (従来どおり、認識テキストがクリップボードに残る)。

- 復元対象: テキスト (Unicode/ANSI)、RTF、HTML、CSV、ファイル一覧 (FileDrop)、ビットマップ。
  元が空なら空に戻す。それ以外の独自形式は復元しない
- 貼り付け (Ctrl+V) の約600ms後に復元する。その間にユーザーや他アプリがクリップボードを
  変更していた場合は、新しい内容を優先して復元しない
- Ctrl+V を送信できなかった場合は、その場で元の内容に戻してからエラーを報告する
- 復元しない場合: `ClipboardOnly` (明示コピー、貼り付け先がなくクリップボードに残した場合)、
  対応形式を含まない・読み取れない・大きすぎる (テキスト計100万文字超、4096px超の画像) クリップボード
- 復元が有効な間は、連続した出力を1件ずつ順に処理する (次の出力は前の復元を待つ)
- 元の内容は1回の貼り付けの間だけメモリに保持し、ファイルやログには書き出さない

## 実行方法

リポジトリ直下の `FeatherScribe.cmd` をダブルクリックすると、必要に応じてビルドしてから起動する (初回はパッケージ取得のため時間がかかる)。

起動時にコンソールで「LLM整形を使いますか？」と確認する (Enter の既定は設定の `llm.enabled`)。

- 使わない (OFF): FeatherScribe を LLM整形オフで即時起動する
- 使う (ON): Ollama が起動していなければ `local/ollama` の Ollama を起動し、インストール済みのモデルを番号付きで表示する
  (設定の `llm.model` が既定)。選んだモデルの読み込み完了を待ってから、LLM整形オンでそのモデルを使って FeatherScribe を起動する
- Ollama が見つからない・応答しない・モデルがない・読み込みに失敗した場合は理由を表示し、LLM整形なしで起動するかを確認する
- この方法で起動した Ollama は FeatherScribe の終了時に停止する。起動前から動いていた Ollama は停止しない
- 選んだ内容はこの起動だけに使い、設定ファイルは変更しない。LLM整形を有効にした起動では、`llm.keepAlive` が未設定なら
  モデルを 60 分保持する (`60m`、整形の間隔が空いても再読み込みで待たないため)
- 確認なしで起動したい場合は引数を付ける。例: ショートカットのリンク先を `FeatherScribe.cmd --llm off` にすると常に LLM整形なしで即時起動する
  (`--llm on --model gemma4:e2b` でモデルも指定できる)
- デスクトップにショートカットを作る場合は `FeatherScribe.cmd` を指定する
- 既に起動している場合は二重起動せず、メッセージを表示して閉じる。起動中のアプリはタスクトレイアイコンから操作する
- 開発時は従来どおり `dotnet run` でも起動できる (この場合は設定ファイルどおりに起動し、コンソールでの確認はない)

```powershell
dotnet run --project src/FeatherScribe.App
```

起動するとメイン画面とタスクトレイアイコンが表示される。
ホットキーを押すと録音開始、もう一度押すと停止し、文字起こし→整形→貼り付けが実行される。
メイン画面の閉じるボタン、またはトレイメニューの「終了」でアプリを終了する。メイン画面はトレイメニューの「画面を表示」から再表示できる。

### 画面と通知

- メイン画面の「状態」に現在の処理状態を、「直近の結果」にコピー・貼り付け対象のテキストを表示する
- メイン画面での操作の結果 (コピー・貼り付け・再整形・整形候補の採用) は、画面下部の短い通知 (スナックバー) で知らせる
- 録音を始めると画面下部中央にオーバーレイを表示し、録音・文字起こし・整形・貼り付けの状態を示す。
  録音中は入力音量に連動するレベルメーターを表示する。オーバーレイは入力先のあるモニターに表示し、フォーカスを奪わない
- 1回の録音は最大5分 (`recording.maxRecordingSeconds`)。上限に達すると録音を自動停止し、それまでの音声を文字起こしする。
  このときオーバーレイに「録音上限（5分）で停止・文字起こし中」、トレイ通知「録音上限に達しました」で知らせる。
  処理が終わるまでのホットキー押下は無視される。続きは、文字起こしが終わってからもう一度ホットキーを押して録音する
- 貼り付け時に FeatherScribe 自身が前面にある場合は自身へ貼り付けず、直前の入力先へ戻してから貼り付ける。
  入力先が見つからない場合はクリップボードにコピーだけ行い、貼り付けできなかったことを知らせる

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
| `Ctrl+Shift+F7` | 選択テキスト編集 | 選択中のテキストを `selectionEdit.mode` (既定 Polite) で整形し、選択範囲を置き換える (録音しない) |

- 既定値はIME・言語切替と衝突しにくい組み合わせを選んでいる。それでも環境により
  登録に失敗した場合は、**該当キーのみ無効化**して起動を継続し、通知とメイン画面・
  `logs/events.log` で失敗内容 (mode / hotkey / Win32エラー) を確認できる。
- `llm.enabled: false` (既定) の間は、全モードがNoFormat相当 (未整形で即貼り付け) になる。
- PlainFast/PlainQualityは既定で **rawを先に貼り付けた時点で完了**し、整形は
  バックグラウンドで実行される (整形中でも次の録音を開始できる)。整形完了は通知され、
  結果は「クリップボードにコピー」または「直前の入力先へ貼り付け」で利用する (入力欄の自動置換はしない)。
- 整形結果が安全基準で採用されなかった場合もrawは保持され、メイン画面またはトレイメニューの
  「再整形」で再試行できる。再整形は直前と同じモードで行い、PlainFast の整形結果が採用されなかった
  場合だけ高品質モード (PlainQuality) に切り替える (`llm.enabled: true` かつ NoFormat 以外の結果があるときのみ)。
- 安全基準で採用されなかった整形候補はメイン画面の「整形候補・警告」に表示される。
  内容を確認して問題ない場合だけ「整形候補を採用」で「直近の結果」として使える
  (トレイメニューの「直近の結果をクリップボードにコピー」「直近の結果を直前の入力先へ貼り付け」も同じ結果を使う)。
- 変更は `config/appsettings.json` の `hotkeys` で行う。

### 選択テキストの編集 (`Ctrl+Shift+F7`)

すでに書いた文章 (Codex への指示、ChatGPT のプロンプト、メール、レビューコメントなど) を、
選択してホットキーを押すだけで整形し直す。`llm.enabled: true` のときだけ使える。

1. 対象アプリで文章を選択し、`Ctrl+Shift+F7` を押す (キーを離すまで待ってからコピーする)
2. FeatherScribe が選択範囲をコピーし、`selectionEdit.mode` (既定 `Polite` = 丁寧文) で整形する
3. 整形結果で選択範囲を置き換え、元のクリップボードの内容を戻す (`output.restoreClipboard` の設定に関係なく戻す)

- 選択テキストを取得できない場合 (選択なし、FeatherScribe 自身が前面、VSCode で選択なしの行コピー) は何もしない
- 整形に失敗・タイムアウト・安全基準で不採用になった場合は置き換えない (選択テキストはそのまま)
- 整形中に入力先のウィンドウが変わった、録音を始めた、もう一度ホットキーを押した場合は置き換えず、
  整形結果をクリップボードに残す
- 結果はオーバーレイで知らせる (整形できなかった・置き換えできなかった・入力先が変わった場合はトレイにも通知)。選択テキストと整形結果はログやファイルに保存しない
- 対応形式のないクリップボード (大きすぎる画像など) は戻せない。Ctrl+Z は対象アプリ側の取り消しになる

## トラブルシュート

| 症状 | 対処 |
| --- | --- |
| 「whisper-cli が見つかりません」 | `tools/setup_whisper.ps1` を実行し、`asr.whisperExecutablePath` を確認 |
| 「LLM へ接続できません」 / `localhost:11434` に拒否された | 別PowerShellで `powershell -ExecutionPolicy Bypass -File tools/start_ollama_server.ps1` を実行し、開いたままにしてからアプリを起動する。`llm.endpoint` も確認。Ollama停止中でも raw transcript で貼り付けは動作する |
| Ollamaが HTTP 500 (out-of-memory) | VRAM不足。`llm.gpuLayers` を `0` にしてCPU実行にする |
| 整形が遅い / タイムアウトする | CPU実行 (GPU未使用) の検証機での実測: 約230文字の発話でe2b約9秒・e4b約15秒、モデル読み込み直後の初回はe2b約19秒・e4b約33秒 (短文ならe2b約4秒)。タイムアウト (`llm.timeoutSeconds`、既定8秒) 時は即raw transcriptが使われ「整形失敗・未整形で貼り付け」と通知される。`rawFirstPaste: true` (既定) ならrawが先に貼り付くため待ちは発生しない。タイムアウトが続く場合は `appsettings.local.json` で `llm.timeoutSeconds` を延ばす |
| 貼り付けされない (コピーはされる) | 対象アプリが Ctrl+V を受け付けるか確認。手動 Ctrl+V で回収可能。管理者権限アプリへは通常権限から送信できない |
| ホットキーが効かない | 他アプリと衝突。起動時の通知を確認し `hotkeys` を変更 |
| 選択テキスト編集が「編集できなかったため…」になる | 整形が `llm.timeoutSeconds` (既定8秒) 内に終わっていない。CPU実行 (GPU未使用) の検証機ではe2bでも約4〜9秒 (モデル読み込み済みの場合) かかるため、長い選択範囲やモデル読み込み直後は8秒を超えることがある。起きる場合は `appsettings.local.json` で `llm.timeoutSeconds` を延ばすか、GPU環境で使う |
| 失敗の詳細を知りたい | `logs/events.log` (メタデータのみ) を確認 |

## プライバシー方針

- 全処理がローカルで完結。クラウド送信なし
- 音声・生文字起こし・整形後テキストはデフォルト保存しない
- 保存するのはメタデータのみ (日時・処理時間・成否・モード・文字数)
- 詳細: [docs/privacy_policy_local.md](docs/privacy_policy_local.md)

## 既知の制約

- 初期版はWindows専用
- 初期版はクリップボード貼り付け方式 (`Ctrl+V` 送信)
- 選択テキスト編集はクリップボード経由のコピー・貼り付け方式 (アプリ別の互換性は手動確認が未実施)
- リアルタイム字幕は未対応
- 1回の録音は最大5分 (`recording.maxRecordingSeconds`、既定 `300`)。上限で自動停止し、オーバーレイとトレイ通知で知らせる。
  上限後の発話は録音されないため、続きは文字起こしが終わってからもう一度ホットキーを押して録音する
- 文字起こしのタイムアウトは録音の長さに応じて延びる (録音秒×3+30秒、`asr.timeoutSeconds` が最小値)。
  CPU実行の whisper は録音時間の約0.5〜1.1倍の処理時間がかかるため、5分の録音では文字起こしに数分待つことがある
- Gemma 4の整形品質・速度はモデルサイズとPC性能に依存する。CPU実行 (GPU未使用) の検証機での実測は約230文字の発話でe2b約9秒・e4b約15秒 (モデル読み込み直後はe2b約19秒・e4b約33秒)。既定タイムアウト8秒を超えることがあるため、LLM整形は既定OFF
- whisper.cppの文字起こし品質はモデルサイズと音声品質に依存する
- 肉声での手動受け入れテストは**未実施** ([docs/acceptance_test.md](docs/acceptance_test.md) 参照。マイク録音→whisper連携はループバックテストで確認済み)

## 開発

```powershell
dotnet build FeatherScribe.slnx
dotnet test FeatherScribe.slnx --filter "Category!=Integration"   # ユニットテスト
dotnet test FeatherScribe.slnx --filter "Category=Integration"    # whisper/Ollama疎通 (ローカルスタック必須)
dotnet test src/FeatherScribe.GuiTests/FeatherScribe.GuiTests.csproj   # GUIテスト (FlaUI、Windows GUI環境必須)
```

GUIテストは実際に FeatherScribe を起動して操作するため、ソリューション (`FeatherScribe.slnx`) には含めず、上記のように明示的に実行します。

### ソースアーカイブ

```powershell
powershell -ExecutionPolicy Bypass -File tools/archive_featherscribe.ps1
```

出力先は既定で環境変数 `FEATHERSCRIBE_ARCHIVE_OUTPUT_DIR`（未設定時はリポジトリと同じ階層の `OutputPath\FeatherScribe` フォルダ）です。`-OutputDirectory` で明示的に指定することもできます。出力先フォルダ内の既存ファイルは作成前に削除されるため、アーカイブ専用のフォルダを指定してください。

### ASRベンチマーク

whisperモデルを固定コーパス (`benchmark/asr/corpus.json`, 30発話) で比較するツール。リポジトリ直下で実行する。

```powershell
dotnet run --project tools/AsrBenchmark -- record                       # 本人の声で30発話を録音 → local/benchmark/audio/
dotnet run --project tools/AsrBenchmark -- run --models "local/models/ggml-small.bin;local/models/ggml-medium.bin"
dotnet run --project tools/AsrBenchmark -- synthesize                   # TTS音声 (ツール動作確認専用) → local/benchmark/tts/
```

結果は `reports/asr-benchmark/<日時>/` (`results.json`, `report.md`) に出力され、Gitには含めない。
TTS音声での結果はツール検証専用で、モデル採用の根拠にしない。手順・指標・採用判断は
[docs/research/asr_model_benchmark.md](docs/research/asr_model_benchmark.md) を参照。

## ローカルLLM整形モデルのベンチマーク

ローカルの整形モデルを比較するベンチマークツール。
既定の実行経路は引き続き whisper.cpp + raw貼り付けである。
ローカルLLM整形はオプションであり、ベンチマークのレポート確認と、出力サンプルの人による確認を経てから選択する。

推奨手順:

1. 候補モデルを Ollama で取得する
2. ベンチマークツールを実行する
3. 生成されたレポートと出力サンプルを確認する
4. 高速モデルと、必要に応じて高品質モデルを選ぶ
5. `appsettings.json` の更新はユーザーの承認後にのみ行う

```powershell
powershell -ExecutionPolicy Bypass -File tools/benchmark_format_models.ps1
```

```bash
tools/benchmark_format_models.sh
```

レポートは `reports/` に出力され、既定でGitには含めない。
自動スコアだけでモデルを採用しない。最終的な採用には、生成サンプルについて次の点を人が確認する必要がある:
意味が保たれていること、言い直しが整理されていること、明らかな助詞の誤りが修正されていること、固有名詞が崩れていないこと、不確かな数値・時刻を推測で補っていないこと。

## 関連文書

- アーキテクチャ: [docs/architecture.md](docs/architecture.md)
- 調査結果: [docs/research/local_stack_research.md](docs/research/local_stack_research.md)
- 受け入れテスト: [docs/acceptance_test.md](docs/acceptance_test.md)
- ライセンス方針: [docs/licensing.md](docs/licensing.md)
- ロードマップ: [docs/roadmap.md](docs/roadmap.md)
