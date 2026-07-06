# 受け入れテスト

最終更新: 2026-07-04

## 1. 自動実施済みテスト

### 1.1 実マイク・ループバックテスト (2026-07-04 実施)

スピーカーからTTSサンプル音声 (`samples/audio/sample_001.wav`) を再生し、
**実マイク (Realtek(R) Audio)** で20秒録音 → whisper.cpp (ggml-small) で文字起こし。

| 項目 | 結果 |
| --- | --- |
| マイクデバイス検出 | ✅ 2台 (USB Live camera audio / Realtek(R) Audio) |
| 録音 (16kHz/16bit/mono) | ✅ 最大振幅7519 (無音でないことを確認) |
| whisper.cpp文字起こし | ✅ exit code 0、日本語テキスト取得 |
| 認識品質 | ⚠️ スピーカー→空気→マイク経路では劣化あり (「ホットキー」→「オットキー」等)。人の肉声を近距離で話す場合はこれより良い条件になる想定 |

### 1.2 その他の自動検証

- ユニットテスト 79件 (フォールバック・raw先貼り付け・辞書・検証器・設定ローダー等): ✅
- 統合テスト 4件 (whisper.cpp CLI疎通 / Ollama API疎通 / フォールバック): ✅
- アプリ起動スモークテスト (常駐・クラッシュなし): ✅
- クリップボード出力・Ctrl+V送信の対アプリ実機確認: **未実施** (下記手動テスト)

## 2. 手動受け入れテスト (未実施・要人手)

> ⚠️ 以下は実際に人が発話して確認する必要があるため **未実施**。
> 実施後、この表を更新すること。

手順: `dotnet run --project src/FeatherScribe.App` で起動し、対象アプリの入力欄に
フォーカスを置いて `Ctrl+Shift+F8` (NoFormat) で録音→再押下で停止。

| # | テスト項目 | モード | 結果 | 備考 |
| --- | --- | --- | --- | --- |
| 1 | メモ帳への貼り付け | NoFormat | 未実施 | |
| 2 | Chrome入力欄への貼り付け | NoFormat | 未実施 | |
| 3 | ChatGPT入力欄への貼り付け | NoFormat | 未実施 | |
| 4 | VSCodeへの貼り付け | NoFormat | 未実施 | |
| 5 | 早口の日本語 | NoFormat | 未実施 | |
| 6 | 言い直しを含む日本語 | PlainFast (llm.enabled=true時) | 未実施 | 言い直しの最終意図が採用されるか |
| 7 | 固有名詞: PhoenixQuant / FeatherScribe / whisper.cpp / Gemma 4 / Codex | NoFormat + 辞書 | 未実施 | dictionary.json の補正を確認 |
| 8 | Gemma停止中のfallback (Ollama停止状態でPlainFast) | PlainFast | 未実施 | raw transcriptで貼り付けされること |

### LLM整形を有効にする場合

`config/appsettings.json` で `llm.enabled: true` にする。
本マシンのCPU実行では E2B でも約50秒/発話のため、`rawFirstPaste: true` (既定) により
rawが先に貼り付き、整形完了後に通知から再コピーする運用になる。
