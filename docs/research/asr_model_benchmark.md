# ASRモデル ベンチマーク (Phase QUALITY-1)

## 目的

本番の whisper モデル (`ggml-small`) は Phase 0 で TTS サンプル1件から選んだもの。
利用者の実際の口述で「直す手間が最も少ない」モデルを、再現可能な手順で選び直す。

## 結論 (採用モデル)

**保留 — 現行の `ggml-small` を継続**

- 2026-10-01 時点の判断: 検証機 (CPU実行) では medium / large-v3-turbo が small の約3〜5倍遅く (RTF 3〜5)、
  日常利用に耐えないため、モデル確定は性能の十分な環境で実音声30発話がそろった時点まで保留する
- 本番既定は引き続き `local/models/ggml-small.bin` (`config/appsettings.json` は変更しない)
- 判断は利用者本人の録音 (`record`) による実音声ランの後に、このセクションへ集計値と理由を記入して確定する
- TTS (合成音声) の結果はツールの動作確認専用で、採用判断には使わない
- UIにモデル切り替えは設けない (採用モデル1つを既定にする)

## 方法

### コーパス

`benchmark/asr/corpus.json` (コミット対象、個人情報なし)。30発話、各要素は
`{ "id", "category", "text", "focusTokens", "readingNote"(任意) }`。

| カテゴリ | 件数 | 内容 |
| --- | ---: | --- |
| general | 3 | 一般的な業務文 |
| time | 3 | 午後3時15分 / 9時30分 / 23時59分 |
| date | 2 | 2026年10月1日 / 12月25日 |
| number | 3 | 1,280円 / 3.5% / 42個・150個 |
| punctuation | 2 | 読点・句点・疑問符の多い文 |
| self-correction | 3 | 言い直し (「明日、いや明後日の10時に」など) |
| fast | 2 | 早口で読む (`readingNote: "早口で"`) |
| short | 3 | 8文字以下 |
| long | 2 | 80文字以上 |
| alphanumeric | 2 | API v2 / GPT-5 / Claude |
| technical | 2 | .NET / C# / WPF / JSON |
| proper-noun | 3 | PhoenixQuant / FeatherScribe / whisper.cpp / Ollama / Codex |

`focusTokens` は文字起こしにそのまま現れるべき表記 (数値・時刻・日付・固有名詞・技術用語)。

### 実行条件

- whisper-cli v1.9.1、本番と同じ引数 (`WhisperCppCommandBuilder.BuildArguments`、言語 `ja`、既定4スレッド)
- 各モデルで計測前に1回の未計測ウォームアップ
- 1発話ごとに whisper-cli を1回起動 (本番と同じくモデル読み込みを含む実時間)
- 候補: `small` (現行) / `medium` / `large-v3-turbo` (メモリ・速度が厳しい場合は `large-v3-turbo-q5_0`)。
  互換性は whisper-cli v1.9.1 で実際に実行して確認する

### 指標

| 指標 | 定義 |
| --- | --- |
| CER | 正規化後の Levenshtein(参照, 認識) / 参照長 (Unicodeスカラー単位、両方空なら0)。正規化 = NFKC → 空白と `、。，．,.!?！？「」『』()（）・…` を除去 (長音 `ー` は残す) → ASCII小文字化 |
| 数値トークン正解率 | 数字を含む focusToken (時刻・日付・金額・`API v2`・`GPT-5` など) のうち、認識結果に含まれた割合 (NFKC・大文字小文字・空白を無視した部分一致、全発話の合計で算出) |
| 固有名詞トークン正解率 | 数字を含まない focusToken (固有名詞・技術用語) について同上 |
| 平均レイテンシ | whisper-cli 起動から終了までの実時間の平均 (成功した発話のみ) |
| RTF | 合計レイテンシ / 合計音声長 (成功した発話のみ)。日常利用では 0.6 以下が望ましい |
| ピークメモリ | whisper-cli プロセスの PeakWorkingSet64 をプロセス終了まで標本化した最大値 |
| 失敗数 | exit code ≠ 0、タイムアウト、出力なし、音声ファイルなし |

失敗した発話は認識結果を空として CER = 1・トークン命中0で品質指標に含める。

レポートの「decision helper」は 失敗数(少) → 数値+固有名詞正解率(高) → 平均CER(低) → RTF ≤ 0.6 を優先 → 平均レイテンシ(低)
の順で並べる参考順位で、最終判断は人がこの文書に記録する。

## 手順

リポジトリ直下で実行する。

```powershell
# 1. 録音 (本人の声。各文を表示 → Enterで録音開始 → Enterで停止。r で録り直し)
dotnet run --project tools/AsrBenchmark -- record
dotnet run --project tools/AsrBenchmark -- record --only u05,u12   # 一部だけ録り直す

# 2. 候補モデルの準備 (未取得の場合)
powershell -ExecutionPolicy Bypass -File tools/setup_whisper.ps1 -ModelSize medium
powershell -ExecutionPolicy Bypass -File tools/setup_whisper.ps1 -ModelSize large-v3-turbo

# 3. 比較実行
dotnet run --project tools/AsrBenchmark -- run --models "local/models/ggml-small.bin;local/models/ggml-medium.bin;local/models/ggml-large-v3-turbo.bin"
```

- 録音は `local/benchmark/audio/` (16 kHz mono 16-bit、`audioset.json` に `real` と記録)
- 録音は複数回に分けてよい (`record --only u15,u16,...`)。録音のない発話は失敗ではなく「部分セット」として報告され、部分セットの結果でモデルを確定しない
- 出力は `reports/asr-benchmark/<yyyyMMdd_HHmmss>/` の `results.json` (発話ごとの行・文字起こし含む) と `report.md` (比較表)
- `synthesize` で作る TTS 音声は `local/benchmark/tts/` に `synthetic` として記録され、レポート冒頭に警告が出る

## プライバシー

録音・文字起こしはローカルのみ (gitignore対象のフォルダ) に保存し、コミット・送信しない。
この文書には集計値と判断のみを記載し、文字起こし本文や音声は載せない。
レポートにはOS版数・論理プロセッサ数が含まれるため、文書へ転記する際は集計値のみを載せる。
詳細は [privacy_policy_local.md](../privacy_policy_local.md) を参照。

## ツール検証ラン (合成音声 / 採用判断には使わない)

2026-10-01、Windows標準の日本語TTS音声 (System.Speech) の30発話、`ggml-small` のみ、CPU実行 (GPU未使用)・4スレッドの検証機。

| Model | Mean CER | Median CER | 数値正解率 | 固有名詞正解率 | 平均レイテンシ | RTF | ピークメモリ | 失敗 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| ggml-small (synthetic) | 0.078 | 0.000 | 81.3% | 38.5% | 5859 ms | 1.06 | 775 MB | 0/30 |

ツールが全コマンド・全指標を出力できることの確認のみ。TTS の読み方 (英字・記号の読み上げ) に強く依存するため、
モデルの優劣や採用の根拠にはしない。

### 3モデル比較 (合成音声 / 互換性・速度感の確認)

同日、同じTTS音声・同条件で `small` / `medium` / `large-v3-turbo` を実行。3モデルとも whisper-cli v1.9.1 で
失敗なく動作した (互換性確認済み)。

| Model | Mean CER | 数値正解率 | 固有名詞正解率 | 平均レイテンシ | RTF | ピークメモリ | 失敗 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| ggml-small | 0.078 | 81.3% | 38.5% | 5634 ms | 1.02 | 776 MB | 0/30 |
| ggml-medium | 0.049 | 81.3% | 46.2% | 16215 ms | 2.94 | 2096 MB | 0/30 |
| ggml-large-v3-turbo | 0.056 | 81.3% | 46.2% | 23561 ms | 4.27 | 1827 MB | 0/30 |

所見 (参考):
- CPU実行 (GPU未使用)・4スレッドの検証機では medium / large-v3-turbo は small の約3〜4倍遅く、RTF ≤ 0.6 をどのモデルも満たさない。
  1発話ごとにモデルを読み込む本番方式のため、短い発話ほどRTFが悪化する
- 固有名詞 (PhoenixQuant / FeatherScribe / whisper.cpp / Ollama / Codex) はどのモデルでも大半を外しており、
  モデル変更より個人辞書 (post-pass置換) での補正が有効な可能性が高い
- 実音声での判断時は、精度差が速度低下 (約3倍) に見合うかを確認すること

## 実音声ラン

(未実施 — 録音後にここへ集計表と採用判断を記入する)
