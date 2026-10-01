# 受け入れテスト

最終更新: 2026-10-01

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

- ユニットテスト 101件 (フォールバック・raw先貼り付け・辞書・検証器・設定ローダー等): ✅
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
CPU実行 (GPU未使用) の検証機では E2B でも約4〜9秒/発話 (モデル読み込み直後は約19秒) かかり、
既定タイムアウト8秒を超えることがあるため、`rawFirstPaste: true` (既定) により
rawが先に貼り付き、整形完了後に通知から再コピーする運用になる。

## 3. Phase UI-6 日常利用 手動受け入れチェックリスト (未実施・要人手)

> ⚠️ 実際に人が発話し、実機の対象アプリで確認する必要があるため **未実施**。
> 自動テスト (static gate / FlaUI GUI gate) はこの表の代わりにならない。実施後、この表を更新すること。

前提:

- `dotnet run --project src/FeatherScribe.App` で起動する。PlainFast・raw first paste・バックグラウンド整形・再整形・整形候補の採用は `config/appsettings.json` で `llm.enabled: true` (`rawFirstPaste: true` 既定) にして確認する。
- 対象アプリの入力欄にフォーカスを置き、ホットキー (既定: NoFormat `Ctrl+Shift+F8` / PlainFast `Ctrl+Shift+F9`) で録音 → 再押下で停止する。
- 「コピー」「貼り付け」「再整形」は MainWindow のボタンとトレイメニューの両方で確認する。

確認項目と期待結果:

| 確認項目 | 期待結果 |
| --- | --- |
| NoFormat | 未整形の文章が対象アプリの入力欄へ貼り付けられる。状態表示は `完了`。 |
| PlainFast | 録音中の状態表示が `録音中…（整形・軽量）`。未整形の文章が先に貼り付けられる。 |
| raw first paste | 貼り付け直後の状態表示が `未整形の文章を貼り付けました・整形中…`、オーバーレイは `貼り付け完了・整形中`。 |
| background format completion | 自動では置き換わらない。状態表示 `整形完了・コピーまたは貼り付けできます`、トレイ通知 `整形完了`。 |
| copy | `直近の結果` がクリップボードに入り、対象アプリで手動 Ctrl+V すると貼り付けられる。結果がない間はボタン・トレイ項目が無効。 |
| repaste | トレイアイコンをクリックした後でも、タスクバーではなく直前の入力先 (対象アプリ) へ貼り付けられる。 |
| reformat | NoFormat の結果や LLM 無効時は `再整形` が無効。PlainFast の結果では有効で、状態表示が `再整形中…` (候補却下後は `再整形中…（高品質）`)。 |
| rejected candidate adopt | 整形候補がある時だけ `整形候補を採用` が有効。採用後、`直近の結果` が整形候補になり状態表示が `整形候補を採用済み`。 |

実施記録:

| # | 対象アプリ | 確認項目 | 結果 | 備考 |
| --- | --- | --- | --- | --- |
| 1 | Notepad | NoFormat | 未実施 | |
| 2 | Notepad | PlainFast | 未実施 | |
| 3 | Notepad | raw first paste | 未実施 | |
| 4 | Notepad | background format completion | 未実施 | |
| 5 | Notepad | copy | 未実施 | |
| 6 | Notepad | repaste | 未実施 | |
| 7 | Notepad | reformat | 未実施 | |
| 8 | Notepad | rejected candidate adopt | 未実施 | |
| 9 | Chrome text input | NoFormat | 未実施 | |
| 10 | Chrome text input | PlainFast | 未実施 | |
| 11 | Chrome text input | raw first paste | 未実施 | |
| 12 | Chrome text input | background format completion | 未実施 | |
| 13 | Chrome text input | copy | 未実施 | |
| 14 | Chrome text input | repaste | 未実施 | |
| 15 | Chrome text input | reformat | 未実施 | |
| 16 | Chrome text input | rejected candidate adopt | 未実施 | |
| 17 | ChatGPT text input | NoFormat | 未実施 | |
| 18 | ChatGPT text input | PlainFast | 未実施 | |
| 19 | ChatGPT text input | raw first paste | 未実施 | |
| 20 | ChatGPT text input | background format completion | 未実施 | |
| 21 | ChatGPT text input | copy | 未実施 | |
| 22 | ChatGPT text input | repaste | 未実施 | |
| 23 | ChatGPT text input | reformat | 未実施 | |
| 24 | ChatGPT text input | rejected candidate adopt | 未実施 | |
| 25 | VSCode editor | NoFormat | 未実施 | |
| 26 | VSCode editor | PlainFast | 未実施 | |
| 27 | VSCode editor | raw first paste | 未実施 | |
| 28 | VSCode editor | background format completion | 未実施 | |
| 29 | VSCode editor | copy | 未実施 | |
| 30 | VSCode editor | repaste | 未実施 | |
| 31 | VSCode editor | reformat | 未実施 | |
| 32 | VSCode editor | rejected candidate adopt | 未実施 | |

既存不具合の修正確認 (Phase UI-6):

| # | 確認項目 | 期待結果 | 結果 | 備考 |
| --- | --- | --- | --- | --- |
| 33 | トレイアイコンをクリックしてからトレイの「直近の結果を直前の入力先へ貼り付け」 | タスクバーではなく、直前に使っていた対象アプリへ貼り付けられる | 未実施 | |
| 34 | 録音中に MainWindow を開き、録音を停止する | 対象アプリへ戻って貼り付けられる。戻せない場合は FeatherScribe 自身には貼り付けず、クリップボードに残して `貼り付けできませんでした` を通知する | 未実施 | |
| 35 | トレイの「再整形」を実行不能な状態で開く | 項目が無効表示になり、アプリは落ちない | 未実施 | |
| 36 | キーボード操作 (Tab / Space / Enter) | Tab 順が 直近の結果 → 整形候補・警告 → (展開時) 整形候補・採用ボタン → 再整形 → コピー → 貼り付け → 操作ガイド。フォーカス枠が見え、Expander を Space/Enter で開閉できる | 未実施 | FlaUI は Tab の先頭2つのみ自動確認 |

## 4. Phase UX-1 選択テキスト編集 手動受け入れチェックリスト (未実施・要人手)

> ⚠️ 実機の対象アプリで人が選択・操作して確認する必要があるため **未実施**。
> 自動テスト (fake のクリップボード・キーボードによる単体テスト) はこの表の代わりにならない。アプリ互換性は人手のみで確認する。実施後、この表を更新すること。

前提:

- `config/appsettings.json` (または `appsettings.local.json`) で `llm.enabled: true` にし、Ollama を起動しておく (「LLMオフ」の項目だけ `llm.enabled: false`)。
- 既定のホットキーは `Ctrl+Shift+F7`、整形モードは `selectionEdit.mode` (既定 `Polite` = 丁寧文)。
- 各項目の前に、クリップボードへ目印の文字列 (例 `ORIGINAL-CLIPBOARD`) をコピーしておき、終了後に手動 Ctrl+V で中身を確認する。

確認項目と期待結果:

| 確認項目 | 操作 | 期待結果 |
| --- | --- | --- |
| success | 文章を選択して `Ctrl+Shift+F7` | 選択範囲が整形結果に置き換わる。オーバーレイ `選択範囲を置き換えました`。クリップボードは目印の文字列に戻っている。対象アプリの Ctrl+Z で元の文章に戻せる。 |
| no selection | 何も選択せずに `Ctrl+Shift+F7` (VSCode では行コピー機能が有効なまま) | 何も変わらない (VSCode で行が重複しない)。オーバーレイ `選択テキストを取得できませんでした`。クリップボードは目印の文字列のまま。 |
| LLM off | `llm.enabled: false` で起動し、文章を選択して `Ctrl+Shift+F7` | 何も変わらない。オーバーレイ `LLM整形がオフのため、選択テキストの編集は使えません`。クリップボードは目印の文字列のまま。 |
| target switched | 文章を選択して `Ctrl+Shift+F7`、整形中に別のウィンドウへ切り替える | どのウィンドウにも貼り付けない。オーバーレイとトレイ通知 `入力先が変わったため置き換えませんでした。編集結果はクリップボードにあります`。クリップボードには整形結果が入っている。 |
| validator rejection | 整形が不採用になる文章 (例: 箇条書き化されやすい長文) を選択して `Ctrl+Shift+F7`、または Ollama を止めて実行 | 選択テキストは変わらない。オーバーレイとトレイ通知 `編集できなかったため、選択テキストは変更していません`。クリップボードは目印の文字列に戻っている。 |

実施記録:

| # | 対象アプリ | 確認項目 | 結果 | 備考 |
| --- | --- | --- | --- | --- |
| 1 | Notepad | success | 未実施 | |
| 2 | Notepad | no selection | 未実施 | |
| 3 | Notepad | LLM off | 未実施 | |
| 4 | Notepad | target switched | 未実施 | |
| 5 | Notepad | validator rejection | 未実施 | |
| 6 | Chrome textarea | success | 未実施 | |
| 7 | Chrome textarea | no selection | 未実施 | |
| 8 | Chrome textarea | LLM off | 未実施 | |
| 9 | Chrome textarea | target switched | 未実施 | |
| 10 | Chrome textarea | validator rejection | 未実施 | |
| 11 | ChatGPT text input | success | 未実施 | |
| 12 | ChatGPT text input | no selection | 未実施 | |
| 13 | ChatGPT text input | LLM off | 未実施 | |
| 14 | ChatGPT text input | target switched | 未実施 | |
| 15 | ChatGPT text input | validator rejection | 未実施 | |
| 16 | VSCode editor | success | 未実施 | |
| 17 | VSCode editor | no selection | 未実施 | VSCode の空選択行コピー (`editor.emptySelectionClipboard`) を検出して中止すること |
| 18 | VSCode editor | LLM off | 未実施 | |
| 19 | VSCode editor | target switched | 未実施 | |
| 20 | VSCode editor | validator rejection | 未実施 | |

追加確認 (Phase UX-1):

| # | 確認項目 | 期待結果 | 結果 | 備考 |
| --- | --- | --- | --- | --- |
| 21 | 録音中・処理中に `Ctrl+Shift+F7` | 何も変わらない。オーバーレイ `処理中のため、選択テキストの編集を開始できません` | 未実施 | |
| 22 | 整形中にもう一度 `Ctrl+Shift+F7` | 2回目は `処理中のため、選択テキストの編集を開始できません`。1回目は置き換えず、整形結果をクリップボードに残す | 未実施 | |
| 23 | `Ctrl+Shift+F7` が他アプリと衝突 | メイン画面の操作ガイドに `⚠ Ctrl+Shift+F7（選択テキスト編集）は使えません: …` が表示され、他のホットキーは使える | 未実施 | |
| 24 | `logs/events.log` | `selection_edit` の行に選択テキスト・整形結果が含まれない (状態・理由コード・文字数のみ) | 未実施 | 単体テストでは fake のログで確認済み |
