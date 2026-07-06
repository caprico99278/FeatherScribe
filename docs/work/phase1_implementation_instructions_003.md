# FeatherScribe モデル実験・検証指示書

## 0. 目的

FeatherScribeのローカルLLM整形モデル候補を比較検証し、最も実用性能の高いモデルを選定する。

ただし、Codexは最終承認を行わない。
Codexは検証・測定・比較・推奨まで行い、最終採用はユーザー承認後とする。

現時点のFeatherScribe既定方針は維持する。

```text
既定:
  llm.enabled = false
  raw transcript貼り付けが主経路

変更禁止:
  ユーザー承認前に既定モデルを勝手に変更しない
  ユーザー承認前にappsettings.jsonの本番既定値を変更しない
```

---

## 1. 検証対象モデル

以下を候補として比較する。

```text
Ultra Fast candidates:
- qwen3:0.6b
- gemma3:1b

Fast candidates:
- qwen3:1.7b
- hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M

Balanced candidates:
- qwen3:4b
- llama3.2:3b
- phi4-mini:3.8b
- gemma3:4b

Reference:
- gemma4:e2b
- gemma4:e4b
```

注意:

```text
- 存在しないモデル名を前提にしない。
- 必ず ollama pull / ollama list / ollama show 相当で確認する。
- pullできないモデルは失敗として記録し、検証対象から除外する。
- モデル取得に時間がかかる場合は、その旨を報告する。
- モデルファイルやGGUFファイルをGit管理対象にしない。
```

---

## 2. 評価方針

単純な速度だけで決めない。
FeatherScribe用途では、以下を総合評価する。

```text
1. 意味を変えない
2. フィラーを自然に除去する
3. 句読点を自然に補う
4. 固有名詞を壊さない
5. 余計な説明・前置きを出さない
6. <think>...</think> やメタ出力を出さない
7. タイムアウトしにくい
8. CPU環境でも現実的な処理時間で動く
```

暫定スコア配分:

```text
Quality: 60点
  - 意味保持: 20
  - 日本語自然さ: 10
  - フィラー除去: 10
  - 言い直し整理: 10
  - 固有名詞保持: 10

Performance: 25点
  - elapsed_ms: 15
  - timeout率: 10

Reliability: 10点
  - 余計な前置きなし
  - <think>タグなし
  - 空出力なし
  - エラーなし

Operational Fit: 5点
  - モデルサイズ
  - pull容易性
  - 設定容易性
```

最終的に以下の3カテゴリで候補を出す。

```text
Best Fast Model:
  日常的な軽量整形向け

Best Quality Model:
  待ってでも整形品質を上げたい用途向け

Recommended Default Candidate:
  FeatherScribeでユーザー承認後に設定する候補
```

---

## 3. 実装する検証ツール

以下を追加する。

```text
tools/benchmark_format_models.ps1
tools/benchmark_format_models.sh
src/FeatherScribe.Benchmarks/
samples/format_benchmark/input/
reports/
```

PowerShellが実行できない環境でも検証できるように、`.sh` または .NET CLI ベースの実行経路も用意する。

推奨:

```text
src/FeatherScribe.Benchmarks/
  Console appとして実装
```

PowerShellはそのラッパーでよい。

---

## 4. ベンチマーク入力サンプル

以下の固定サンプルを作成する。

### sample_001_short.txt

```text
えーと、明日の会議なんですけど、まあ10時からでお願いします
```

### sample_002_terms.txt

```text
PhoenixQuantのPhaseでCodexに実装指示を出します。whisper.cppとGemma 4の設定を見直します
```

### sample_003_self_correction.txt

```text
この処理は同期で、いや違う、バックグラウンドで実行してください。ユーザー操作なしで自動置換しないようにしてください
```

### sample_004_business.txt

```text
先ほどの件ですが、えー、確認したところ問題なさそうなので進めてください。必要であれば私の方で追加確認します
```

### sample_005_bullet.txt

```text
今日やることは、READMEの確認、PowerShellの検証、肉声テスト、あとホットキーの見直しです
```

### sample_006_dev_instruction.txt

```text
FeatherScribeのDictationPipelineでOperationIdを使って、古いバックグラウンド整形結果が新しい入力結果を上書きしないようにしてください
```

### sample_007_noise_asr.txt

```text
whisper.cppで文字を越し押して、いや、文字起こしをして、そのあとGemma 4じゃなくて軽量モデルで整形します
```

### sample_008_long_memo.txt

```text
ローカルLLMの整形処理がタイムアウトする場合は、raw transcriptを先に貼り付けて、整形処理はバックグラウンドで実行する方針にします。整形が完了したら通知だけ出して、自動置換はしないようにします
```

---

## 5. プロンプト条件

既存の `prompts/plain.md` を基本に使う。
ただし、ベンチマーク用に余計な出力を防ぐため、以下の制約を追加してもよい。

```text
- 出力は整形後テキストのみ
- 解説しない
- 前置きしない
- Markdown見出しを出さない
- 箇条書き指定がない限り箇条書きにしない
- <think>タグを出さない
- 思考過程を出さない
```

Qwen3系で `<think>...</think>` が出る場合は、以下を行う。

```text
1. まずプロンプトで抑止する
2. それでも出る場合はpost filterで除去する
3. 除去が発生した回数を benchmark result に記録する
```

post filterを追加した場合、通常のFeatherScribe整形経路にも安全に適用できるようにする。

---

## 6. Ollamaリクエスト設定

全モデルで原則同一条件にする。

```json
{
  "stream": false,
  "keep_alive": "30m",
  "options": {
    "temperature": 0.1,
    "num_predict": 128,
    "num_ctx": 1024
  }
}
```

ただし、モデルごとにエラーになるoptionがある場合は、記録したうえで最小限の調整を許可する。

記録項目:

```text
- model
- temperature
- num_predict
- num_ctx
- keep_alive
- timeout_seconds
```

---

## 7. タイムアウト条件

初期タイムアウト:

```text
Fast benchmark timeout:
  30秒

Quality benchmark timeout:
  120秒

Reference benchmark timeout:
  180秒
```

ただし、FeatherScribeの実運用では `timeoutSeconds: 8` が現在の安全設定であることを忘れない。
ベンチマークではモデル性能を測るため、検証用に長めのタイムアウトを使う。

---

## 8. 測定項目

各モデル × 各サンプルについて以下を記録する。

```text
model
sample_id
sample_name
elapsed_ms
timed_out
error
output_text
output_chars
contains_preamble
contains_think_tag
think_tag_removed
changed_terms
missing_required_terms
added_unspoken_content
meaning_changed_suspected
score_quality
score_performance
score_reliability
score_total
```

固有名詞チェック対象:

```text
PhoenixQuant
FeatherScribe
Codex
whisper.cpp
Gemma 4
OperationId
DictationPipeline
```

固有名詞が入力に含まれているのに出力で壊れた場合は減点する。

---

## 9. 自動評価と人間レビュー

自動評価だけで最終判断しない。
以下の2段階にする。

### 9.1 自動評価

機械的に判定する。

```text
- タイムアウト
- 空出力
- 前置き混入
- <think>タグ混入
- 固有名詞欠落
- 出力文字数の異常増加
- 出力文字数の異常減少
```

### 9.2 人間レビュー用出力

モデルごとに、入力と出力を並べたMarkdownを生成する。

```text
reports/format_model_benchmark_YYYYMMDD_HHMMSS.md
reports/format_model_benchmark_YYYYMMDD_HHMMSS.csv
reports/format_model_outputs_YYYYMMDD_HHMMSS.md
```

`format_model_outputs` には以下を載せる。

```text
## model: qwen3:1.7b

### sample_001_short
Input:
...

Output:
...

Auto notes:
...
```

ユーザーが読んで承認できるようにすること。

---

## 10. 合格基準

### Fast Model 合格基準

```text
- 平均 elapsed_ms が 15秒以下
- timeout率 10%以下
- 固有名詞破壊が少ない
- 前置き・説明文を出さない
- <think>タグを出さない、またはpost filterで安全に除去できる
```

### Quality Model 合格基準

```text
- 平均 elapsed_ms が 60秒以下
- timeout率 10%以下
- 意味保持が強い
- 言い直し整理が自然
- ビジネス文・開発指示文の整形が安定
```

### 不採用条件

以下に該当するモデルは不採用。

```text
- timeout率が高い
- 前置きや解説を頻繁に出す
- <think>タグが頻繁に混入する
- 固有名詞を壊す
- 入力にない情報を追加する
- 日本語が不自然
- CPU環境で実用速度に乗らない
```

---

## 11. FeatherScribe本体への変更制限

今回の作業で許可する本体変更:

```text
- Ollama request options を設定可能にする
- keep_alive / num_predict / num_ctx を設定可能にする
- <think>タグ除去post filterを追加する
- model benchmark用のconsole appを追加する
- READMEにモデル検証手順を追記する
```

今回の作業で禁止する変更:

```text
- ユーザー承認前に appsettings.json の既定モデルを変更する
- ユーザー承認前に llm.enabled を true に変更する
- ユーザー承認前に gemma4:e2b を削除する
- 既存のraw貼り付け主経路を壊す
- ホットキー設定を変える
- 自動置換を実装する
- モデルファイルをGitに追加する
```

---

## 12. README追記

READMEに以下を追記する。

```text
## Local LLM model benchmark

FeatherScribe includes a benchmark tool for comparing local formatting models.
The default runtime path remains whisper.cpp + raw paste.
Local LLM formatting is optional and must be selected after benchmark review.

Recommended workflow:
1. Pull candidate models with Ollama.
2. Run the benchmark tool.
3. Review generated reports.
4. Choose a fast model and optional quality model.
5. Update appsettings.json only after user approval.
```

---

## 13. 実行コマンド例

以下のようなコマンドで実行できるようにする。

```powershell
dotnet run --project src/FeatherScribe.Benchmarks -- `
  --models qwen3:0.6b,qwen3:1.7b,qwen3:4b,llama3.2:3b,phi4-mini:3.8b,gemma3:1b,gemma3:4b,gemma4:e2b `
  --input samples/format_benchmark/input `
  --output reports `
  --timeout-fast 30 `
  --timeout-quality 120
```

PowerShellラッパー:

```powershell
tools/benchmark_format_models.ps1
```

Bashラッパー:

```bash
tools/benchmark_format_models.sh
```

---

## 14. 最終報告に含めること

作業完了時、以下を報告する。

```text
1. pull成功/失敗モデル一覧
2. 各モデルの平均処理時間
3. 各モデルのtimeout率
4. 各モデルの総合スコア
5. 固有名詞破壊の有無
6. <think>タグ混入の有無
7. 前置き・説明文混入の有無
8. 自動評価上のBest Fast Model
9. 自動評価上のBest Quality Model
10. Codexとしての推奨モデル
11. ただし最終採用はユーザー承認が必要であること
12. 生成したreportsファイルのパス
13. appsettings.json変更案
```

appsettings変更案は、実ファイルを書き換えず、Markdownで提示すること。

例:

```json
{
  "llm": {
    "enabled": false,
    "model": "qwen3:1.7b",
    "qualityModel": "qwen3:4b",
    "timeoutSeconds": 12,
    "qualityTimeoutSeconds": 60,
    "fallbackToRaw": true,
    "rawFirstPaste": true,
    "gpuLayers": 0
  }
}
```

---

## 15. 品質ゲート

実装後、以下を実行する。

```bash
git diff --check
dotnet build
dotnet test
dotnet format --verify-no-changes
```

ベンチマーク実行後、以下も確認する。

```text
- reports/ に結果が生成されている
- モデルファイルがGit管理対象になっていない
- reports/ は必要に応じて .gitignore 対象にする
- samples/format_benchmark/input は固定サンプルとしてGit管理対象にする
- benchmark出力CSV/Markdownは原則Git管理対象外
```

---

## 16. コミット方針

実装が完了したら、以下のコミットを作成してよい。

```text
feat: add local formatter model benchmark
```

ただし、以下は禁止。

```text
- tag作成
- release作成
- appsettings.jsonの既定モデル変更
- llm.enabled=true への変更
```

ベンチマーク結果は、ユーザーが確認できるように作成するが、巨大なログやモデルファイルはコミットしないこと。
