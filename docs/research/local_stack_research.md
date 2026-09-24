# ローカルスタック調査結果

確認日: 2026-07-04

## 1. whisper.cpp

### 参照した公式URL

- https://github.com/ggml-org/whisper.cpp
- https://github.com/ggml-org/whisper.cpp/releases (最新: v1.9.1)
- https://huggingface.co/ggerganov/whisper.cpp (ggmlモデル配布)

### 確認結果

| 項目 | 結果 |
| --- | --- |
| ビルド手順 | CMakeビルドも可能だが、Windowsは公式リリースのプリビルドzipで十分 |
| Windows推奨導入 | `whisper-bin-x64.zip` (v1.9.1, 約8MB) をダウンロードして展開 |
| CLI実行ファイル名 | `Release/whisper-cli.exe` (旧 `main.exe` は非推奨だが同梱) |
| モデル取得 | Hugging Face `ggerganov/whisper.cpp` から `ggml-<size>.bin` を直接ダウンロード |
| 日本語指定 | `-l ja` |
| WAV入力時の出力 | `--output-txt --output-file <base>` で `<base>.txt` (UTF-8) を生成。`--no-prints` で進捗ログ抑制 |
| 入力形式 | 16kHz/16bit/mono WAV を推奨 (録音側でこの形式に合わせる) |

### 実行したコマンド

```text
curl -L -o whisper-bin-x64.zip https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.1/whisper-bin-x64.zip
curl -L -o ggml-small.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin
whisper-cli.exe -m local/models/ggml-small.bin -l ja -t 4 --output-txt --output-file samples/raw/sample_001_raw --no-prints -f samples/audio/sample_001.wav
```

### 成功/失敗

成功。約35秒の日本語音声 (TTS生成) を CPU (4スレッド) で 16.5秒 で文字起こし。
実時間比 約0.5x で、実用速度。

### 採用判断

- **採用**: whisper.cpp v1.9.1 プリビルド + `ggml-small.bin` (466MB)
- smallモデルで日本語の認識品質は実用候補 (「文字起こし」→「文字を越し押して」等の軽微な誤認識はある)。ただしGemma 4整形は意味回復を保証しないため、ASR精度・辞書補正・raw fallbackを前提に扱う。
- 品質が必要なら `ggml-medium` 以上へ差し替え可能 (設定ファイルでパス変更のみ)

## 2. Gemma 4 + Ollama

### 参照した公式URL

- https://ollama.com/library/gemma4 (タグ一覧)
- https://github.com/ollama/ollama/releases (最新: v0.31.1)
- https://ai.google.dev/gemma/docs/integrations/ollama

### 確認結果

| 項目 | 結果 |
| --- | --- |
| モデル名 | `gemma4` (2026-04-02 リリース、Apache 2.0) |
| タグ | `e2b` (7.2GB) / `e4b` (9.6GB, 既定) / `12b` (7.6GB) / `26b` (18GB) / `31b` (20GB) |
| API | `POST /api/chat` を採用 (`/api/generate` はレガシー寄り。会話形式の方がプロンプト管理が素直) |
| パラメータ | `options.temperature` で指定。`options.num_gpu` でGPUオフロード層数を制御 |
| タイムアウト挙動 | サーバー側は応答を返すまでブロック。クライアント側でHTTPタイムアウト管理が必須 |
| 死活確認 | `GET /api/version` |

### 実行したコマンド

```text
ollama serve   (v0.31.1, zip展開版)
ollama pull gemma4:e4b
ollama pull gemma4:e2b
POST http://localhost:11434/api/chat  (stream=false, temperature=0.1, num_gpu=0)
```

### 成功/失敗

- GPU (Vulkan) 実行: **失敗**。VRAM不足で `llama-server reported out-of-memory during startup` (HTTP 500)
- CPU実行 (`num_gpu: 0`): **成功** (API疎通・整形出力の取得)

整形品質の実測 (同一raw transcriptに対する出力比較):

- 共通して確認できたこと:
  - 辞書注入により「ウイスパー」→「whisper.cpp」「ジェマ」→「Gemma 4」を置換
  - 前置き・解説の混入なし (FormatResultValidatorも通過)
- **回復できなかったこと (重要)**:
  - `samples/formatted/sample_001_formatted.txt` (gemma4:e2b の出力) では、
    フィラー「Aと、」が残り、ASR誤認識「文字を越し押して」も回復できていない
  - gemma4:e4b の試行ではフィラー除去と「文字起こしをして」への補正が見られたが、
    毎回保証されるものではない

結論: **LLM整形を意味回復の保証として扱わない**。
raw transcript fallbackと辞書補正 (pre/post) を併用し、整形は「読みやすさの改善」に留める。

### 採用判断

- **採用**: Ollama v0.31.1、`/api/chat`、`temperature=0.1`
  - 通常モード: `gemma4:e2b` / PlainQuality専用: `gemma4:e4b`
- 本環境ではVRAM不足のため `gpuLayers: 0` (CPU実行) を既定とする
- CPU実行の整形は遅い (§4の実測参照) ため、LLM整形は既定OFF + rawFirstPaste設計とする

## 3. llama.cpp server (代替LLM経路)

### 参照した公式URL

- https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md

### 確認結果

| 項目 | 結果 |
| --- | --- |
| OpenAI互換API | あり: `/v1/chat/completions`, `/v1/completions`, `/v1/embeddings`, `/v1/models` |
| Windows起動 | `llama-server.exe -m <model.gguf> -c 2048` (既定 127.0.0.1:8080) |
| Gemma GGUF | 利用可 (GGUF形式のGemmaモデルをロードできる) |

### 採用判断

- MVPでは**Ollamaを第一経路**とし、llama.cpp serverは将来の差し替え候補として温存
- `ITextFormatter` インターフェースで分離済みのため、OpenAI互換クライアント実装を追加すれば差し替え可能
- 実機疎通は未実施 (Ollamaで縦切りが成立したため優先度を下げた)

## 4. Gemma 4 E2B / E4B 処理時間比較 (2026-07-04 追記)

同一入力 (約230文字のraw transcript + plain.mdプロンプト)、CPU実行 (`num_gpu: 0`)、
`/api/chat`、temperature 0.1 での実測。

| モデル | cold (ロード込み) | warm | 備考 |
| --- | --- | --- | --- |
| gemma4:e2b (7.2GB) | 57.6秒 | 50.6秒 | 既定モデル。E4B比 約2.6倍高速 |
| gemma4:e4b (9.6GB) | 152.3秒 | 133.5秒 | PlainQuality専用 |

結論:

- 本マシン (GPU不可・CPU実行) では **E2Bでも既定タイムアウト8秒に収まらない**。
  よって初期設定は `llm.enabled: false` とし、有効化した場合も
  `rawFirstPaste: true` でrawを先に貼り付け、整形はバックグラウンド完了後に
  再コピーで利用する設計とした。タイムアウト時は即raw transcriptを使用する。
- VRAMが十分なGPU環境では `gpuLayers` を調整することで大幅な高速化が見込める (未計測)。

## 5. 実マイク検証 (2026-07-04 追記)

スピーカーでTTSサンプルを再生し実マイク (Realtek(R) Audio) で録音→whisper.cppで
文字起こしするループバックテストを実施。録音経路・whisper連携は動作確認済み。
空気経由の再生音では認識品質が劣化する (詳細は docs/acceptance_test.md)。
**人の肉声による手動受け入れテストは未実施** (同文書の手順に従い実施すること)。

## 6. 未解決リスク

| リスク | 影響 | 緩和策 |
| --- | --- | --- |
| CPU実行のGemma 4整形が遅い (約2〜2.5分/発話) | 長文の実用性が下がる | `gemma4:e2b` や小型モデルへの切替、VRAM十分なGPU環境、`llm.timeoutSeconds=300` に拡大済み、タイムアウト時はraw transcriptへフォールバック |
| GPU (Vulkan) でOOM | GPU高速化が使えない | `gpuLayers` 設定で環境ごとに調整可能にした (0=CPU) |
| Ollamaモデルの初回ロードが遅い (約20秒+) | 初回リクエストがさらに遅延 | Ollamaの keep_alive (既定5分) 内は再利用される。常用時は影響小 |
| whisper smallモデルの誤認識 | 固有名詞が崩れる | 個人辞書 (pre/post補正 + プロンプト注入) で緩和 |
| llama.cpp server経路が未検証 | 代替経路の即応性なし | インターフェース分離済み。必要時に実装・検証 |
| 肉声での手動受け入れテスト未実施 | 実運用の認識率・貼り付け互換性が未確認 | docs/acceptance_test.md の手順で実施する (マイク録音経路自体はループバックで確認済み) |
| tools/*.ps1 が未検証 | セットアップスクリプトの不具合リスク | 本開発セッションはPowerShell実行が許可されていないため未検証。同等処理 (ダウンロード・展開・whisper/Ollama呼び出し) はbash/dotnetで検証済み。初回実行時に確認すること |
