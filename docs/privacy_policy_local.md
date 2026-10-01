# プライバシー方針 (ローカル動作)

## 基本方針

本アプリは、音声入力から貼り付けまでの全処理を **ローカルPC内で完結** させる。
クラウドAPIへの送信は行わない。

## デフォルトで保存しないもの

- 音声ファイル (録音WAVは一時ディレクトリに作成し、処理完了後に削除)
- 生文字起こし全文 (whisper.cppの出力txtは読み取り後に削除)
- 整形後テキスト全文

## デフォルトで保存するもの (logs/events.log)

メタデータのみ:

- 実行日時
- 処理時間 (ミリ秒)
- 成功/失敗
- エラー種別
- 使用モード (Plain / NoFormat など)
- 使用モデル名
- 文字数

本文・音声そのものは記録しない。

## デバッグモード

`config/appsettings.json` で `debug.enabled` を **明示的に true** にした場合のみ、
`privacy.saveAudioFiles` / `saveRawTranscript` / `saveFormattedText` の各フラグに従い
本文系データの保存を許可する。既定はすべて false。

## クリップボードの復元 (output.restoreClipboard)

`output.restoreClipboard` を true にした場合、貼り付け前のクリップボード内容 (スナップショット) を
1回の貼り付けの間だけメモリ上に保持し、その貼り付け処理の終了時 (復元した場合もしなかった場合も) に破棄する。
スナップショットはファイル・ログ (logs/events.log を含む) のどこにも書き出さない。
デバッグ出力に残るのは復元の判断結果と形式数などのメタデータのみで、内容は含まない。

## ASRベンチマークのデータ (tools/AsrBenchmark)

ASRモデル比較用のベンチマークは、利用者が明示的にコマンドを実行した場合にのみデータを作成する (アプリ本体は関与しない)。

- 本人の録音 (`record`) は `local/benchmark/audio/`、TTS音声 (`synthesize`) は `local/benchmark/tts/` にのみ保存する。どちらもgitignore対象で、コミット・送信はしない
- 文字起こし結果を含む `results.json` / `report.md` は `reports/asr-benchmark/` にのみ保存する (gitignore対象)
- 読み上げ文 (`benchmark/asr/corpus.json`) は個人情報を含まない固定文のみ。コミットされる `docs/research/asr_model_benchmark.md` には集計値と採用判断だけを書き、文字起こし本文や音声は載せない
- 削除はフォルダ (`local/benchmark/` と `reports/asr-benchmark/`) を消すだけでよい

## ネットワーク通信

- Ollama API (`http://localhost:11434`) — ローカルループバックのみ
- whisper.cpp — ネットワーク通信なし (ローカルプロセス)
- セットアップスクリプト実行時のみ、バイナリ/モデルのダウンロードで外部接続が発生する
