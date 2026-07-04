# FeatherScribe 修正指示: 初期コミット前の安定化対応

前回レビューで判明した問題を修正する。
目的は、FeatherScribeを「technical MVP baseline」として安全に初期コミットできる状態へ整えること。

今回の修正では、機能追加を広げない。
以下の修正に限定する。

---

## 1. 最重要: rawFirstPaste を本当のバックグラウンド整形に修正する

### 現状の問題

`rawFirstPaste` が有効な場合、raw transcript は先に貼り付けられるが、その後のGemma整形を `await` しているため、パイプライン全体が完了扱いにならない。

現状イメージ:

```text
録音
↓
whisper.cpp
↓
rawを貼り付け
↓
Gemma整形をawait
↓
整形完了までProcessing状態
```

これでは、PlainQualityなどで最大300秒待つ可能性があり、音声入力アプリとして不適切。

### 期待する動作

`rawFirstPaste` が有効な場合は、raw transcriptを貼り付けた時点でメインパイプラインを完了扱いにする。

期待動作:

```text
録音
↓
whisper.cpp
↓
rawを即貼り付け
↓
メインパイプラインは完了
↓
Gemma整形は別Taskでバックグラウンド実行
↓
整形完了後、通知
↓
ユーザー操作で「整形結果を再コピー」または「整形結果を再貼り付け」
```

### 実装要件

* `rawFirstPaste == true` の場合、`DictationPipeline` はGemma整形を同期的に待たないこと。
* バックグラウンド整形中でも、次の録音を開始できること。
* 自動置換は禁止。ユーザー操作なしに既存入力欄を置換しないこと。
* 整形完了後、直近整形結果を保持すること。
* トレイメニュー、または既存UIの「再コピー」「再貼り付け」導線から整形済みテキストを使えること。
* バックグラウンド整形が失敗した場合は通知し、アプリを落とさないこと。
* CancellationTokenの扱いを明確にすること。アプリ終了時はバックグラウンド整形を安全にキャンセルすること。

### テスト追加

以下のユニットテストを追加する。

```text
- rawFirstPaste有効時、formatterが遅延してもPipelineがraw出力後に即完了する
- rawFirstPaste有効時、formatter失敗でもraw出力は成功扱いになる
- background formatting完了後、整形済み結果がLastFormattedResultとして保持される
- background formatting中に次の録音開始をブロックしない
```

---

## 2. Windowsで利用可能なホットキー既定値へ変更する

### 現状の問題

アプリ起動時、Windows環境で利用できない、または既に予約・衝突しているホットキーがあり、起動時にエラーになっていた。

ホットキー登録失敗でアプリ起動に失敗するのはNG。

### 修正方針

ホットキーは環境依存で失敗するため、以下を実装する。

```text
1. 既定ホットキーをWindowsで衝突しにくい組み合わせへ変更する
2. RegisterHotKey失敗時にアプリを落とさない
3. 失敗したホットキーだけ無効化する
4. どのホットキーが失敗したか通知・ログ出力する
5. 設定画面または設定ファイルで変更できる状態を維持する
```

### 推奨デフォルト

既存の `Ctrl + Alt + Space` や `Ctrl + Alt + Shift + Space` は、IME、言語切替、環境依存ショートカットと衝突しやすいため避ける。

初期値は以下に変更する。

```text
NoFormat:
  Ctrl + Shift + F8

PlainFast:
  Ctrl + Shift + F9

PlainQuality:
  Ctrl + Shift + F10

Polite:
  Ctrl + Shift + F11

Bullet:
  Ctrl + Shift + F12

Memo:
  Ctrl + Alt + Shift + M

DevInstruction:
  Ctrl + Alt + Shift + D
```

ただし、これらも環境によって登録失敗する可能性があるため、失敗時の安全処理を必ず実装すること。

### RegisterHotKey失敗時の扱い

Windows API `RegisterHotKey` が失敗した場合、以下のように扱う。

```text
- アプリ起動は継続する
- 該当ホットキーのみ無効化する
- ログに mode / hotkey / Win32 error code を記録する
- ユーザーに「一部ホットキー登録に失敗しました」と通知する
- トレイメニューや設定画面から後で確認できるようにする
```

特に `ERROR_HOTKEY_ALREADY_REGISTERED` 相当のエラーは想定内として扱う。

### 起動時の挙動

NG:

```text
ホットキー登録失敗
↓
例外
↓
アプリ起動失敗
```

OK:

```text
ホットキー登録失敗
↓
該当ホットキーを無効化
↓
警告通知
↓
アプリは起動継続
```

### 設定ファイル例

`config/appsettings.json` の hotkeys 既定値を更新する。

```json
{
  "hotkeys": {
    "NoFormat": "Ctrl+Shift+F8",
    "PlainFast": "Ctrl+Shift+F9",
    "PlainQuality": "Ctrl+Shift+F10",
    "Polite": "Ctrl+Shift+F11",
    "Bullet": "Ctrl+Shift+F12",
    "Memo": "Ctrl+Alt+Shift+M",
    "DevInstruction": "Ctrl+Alt+Shift+D"
  }
}
```

### テスト追加

以下を追加する。

```text
- HotkeyParserが Ctrl+Shift+F8 を正しくparseできる
- HotkeyParserが Ctrl+Alt+Shift+M を正しくparseできる
- RegisterHotKey失敗時にアプリ初期化が例外終了しない
- 一部ホットキー失敗時、成功したホットキーは使える状態になる
- 登録失敗したホットキーがログ/状態に残る
```

必要なら `IHotkeyRegistrar` を抽象化し、テストでは失敗を返すfakeを使うこと。

---

## 3. UTF-8 BOMなし / LF へ統一する

### 現状の問題

`.gitattributes` は追加済みだが、実ファイルにBOMやCRLFが残っている。

確認済みの問題例:

```text
BOM:
- tools/run_asr_test.ps1
- tools/run_format_test.ps1
- tools/setup_gemma_ollama.ps1
- tools/setup_whisper.ps1
- src/FeatherScribe.Core/FeatherScribe.Core.csproj

CRLF:
- src/FeatherScribe.App/AssemblyInfo.cs
- src/FeatherScribe.Core/FeatherScribe.Core.csproj
- samples/raw/sample_001_raw.txt
```

### 修正要件

* 全テキストファイルを UTF-8 BOMなし / LF に統一する。
* `.gitattributes` に加えて `.editorconfig` を追加する。
* PowerShellスクリプトもBOMなしUTF-8で保存する。
* 変換後に差分確認する。

### `.editorconfig` 例

```editorconfig
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true

[*.md]
trim_trailing_whitespace = false

[*.ps1]
charset = utf-8
end_of_line = lf

[*.cs]
charset = utf-8
end_of_line = lf

[*.csproj]
charset = utf-8
end_of_line = lf

[*.slnx]
charset = utf-8
end_of_line = lf

[*.json]
charset = utf-8
end_of_line = lf
```

### 検証

以下を実行し、結果を報告する。

```bash
git diff --check
```

可能であれば、BOM検出・CRLF検出も実施すること。

---

## 4. READMEのパス誤りを修正する

### 現状の問題

READMEに以下の誤記がある。

```text
doc/work/Phase1_実装指示書_001.md
```

実際は以下。

```text
docs/work/Phase1_実装指示書_001.md
```

### 修正要件

* `doc/work` を `docs/work` に修正する。
* 可能であれば、日本語ファイル名をASCII名へ変更する。

推奨:

```text
docs/work/phase1_implementation_instructions_001.md
```

ファイル名を変更した場合は、READMEと関連文書のリンクもすべて更新すること。

---

## 5. profiles.json の扱いを明確化する

### 現状の問題

`config/profiles.json` は存在しているが、現状の `FilePromptProvider` は固定マッピングでpromptを選んでおり、profiles.jsonを読んでいない。

### 修正方針

今回のMVPでは、profiles.jsonの本格対応は不要。
ただし、README/docsで「現在は将来拡張用」と明記すること。

### 追記内容

READMEまたはarchitectureに以下の趣旨を追記する。

```text
config/profiles.json is currently reserved for future app-specific profile support.
The current MVP uses fixed prompt file mapping by FormattingMode.
```

日本語READMEなら以下。

```text
config/profiles.json は将来のアプリ別プロファイル用です。
現行MVPでは FormattingMode ごとの固定プロンプトマッピングを使用します。
```

---

## 6. debug / privacy 設定の未実装部分を明記する

### 現状の問題

`appsettings.json` には以下がある。

```json
"privacy": {
  "saveAudioFiles": false,
  "saveRawTranscript": false,
  "saveFormattedText": false
},
"debug": {
  "enabled": false,
  "saveDirectory": "debug_artifacts"
}
```

しかし現状コードでは、raw/formatted全文保存やdebug.saveDirectoryの扱いが十分に実装されていない。

### 修正方針

今回のMVPでは、本格実装しなくてよい。
ただし、未実装・将来拡張用であることをREADME/docsに明記すること。

追記例:

```text
The current MVP does not persist raw transcripts or formatted text by default.
Some debug persistence settings are reserved for future implementation.
```

---

## 7. sample_001_formatted.txt と調査文書の不一致を修正する

### 現状の問題

`docs/research/local_stack_research.md` では、Gemma整形により以下が回復したと書かれている。

```text
「文字を越し押して」→「文字起こしをして」
```

しかし、実際の `samples/formatted/sample_001_formatted.txt` では以下のように残っている。

```text
whisper.cppで文字を越し押して
```

文書上の成功主張と実サンプルが一致していない。

### 修正方針

過剰な成功主張を避ける。
調査文書を正直な記述に修正すること。

推奨修正:

```text
Gemma 4整形ではフィラー除去と一部表記補正は確認できた。
ただし、このサンプルでは「文字を越し押して」のようなASR誤認識は完全には回復できなかった。
そのため、LLM整形を意味回復の保証として扱わず、raw transcript fallbackと辞書補正を併用する。
```

必要なら、サンプルファイルも再生成してよい。
ただし、文書とサンプルの内容が一致すること。

---

## 8. RestoreClipboard 未実装を明記する

### 現状の問題

`output.restoreClipboard` 設定があるが、元クリップボード復元は実装されていないように見える。

### 修正方針

今回のMVPでは実装しなくてよい。
ただし、READMEまたはroadmapに未実装と明記すること。

追記例:

```text
output.restoreClipboard is reserved for future implementation.
The MVP writes recognized text to the clipboard and does not restore the previous clipboard content.
```

---

## 9. samples配下のgit管理を安全化する

### 現状

`samples/raw/*.txt` と `samples/formatted/*.txt` はサンプルとして意図的に含めるなら許容。

ただし、将来の実データ混入を避けるため、サンプルだけ明示許可にする。

### .gitignore 追記例

```gitignore
# Sample generated artifacts
samples/audio/*
samples/raw/*
samples/formatted/*

# Keep curated sample text files only
!samples/audio/.gitkeep
!samples/raw/.gitkeep
!samples/formatted/.gitkeep
!samples/raw/sample_001_raw.txt
!samples/formatted/sample_001_formatted.txt
```

音声ファイルは原則コミットしないこと。

---

## 10. 最終品質ゲート

修正後、以下を実行して報告すること。

```bash
git status --short
git ls-files --others --exclude-standard
git add -N .
git diff --stat
git diff --check
dotnet build
dotnet test
```

可能なら以下も実行する。

```bash
dotnet format --verify-no-changes
```

`dotnet format` が使えない場合は理由を明記すること。

---

## 11. 報告に含めること

修正後の報告には、以下を必ず含める。

```text
1. rawFirstPasteが本当にバックグラウンド化されたか
2. background formatting中に次の録音を開始できるか
3. 新しい既定ホットキー一覧
4. ホットキー登録失敗時に起動継続できるか
5. BOM/CRLFの再検査結果
6. README/docsの未実装・未検証表記の更新内容
7. sample/research doc不一致の修正内容
8. build/test結果
9. git diff --check結果
10. コミット対象にモデル/音声/バイナリ/ローカル設定が含まれていないこと
```

---

## 12. コミット可否

以下を満たした場合のみ、technical MVP baselineとして初期コミットしてよい。

```text
- FeatherScribe名で統一済み
- appsettings.jsonの既定値が安全
- LLM整形は既定OFF
- rawFirstPasteが本当にバックグラウンド化されている
- ホットキー登録失敗でアプリが落ちない
- 既定ホットキーがWindowsで衝突しにくい値へ変更されている
- build成功
- test成功
- git diff --check成功
- BOMなしUTF-8 / LFへ統一済み
- PowerShell未検証と肉声テスト未完了がREADME/docsに明記されている
- モデル/音声/バイナリ/ローカル設定がコミット対象外
```

推奨コミットメッセージ:

```text
chore: initialize FeatherScribe local voice input MVP
```

タグは付けないこと。
