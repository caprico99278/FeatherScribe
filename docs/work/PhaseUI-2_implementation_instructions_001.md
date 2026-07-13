```id="phase-ui2-main-window"
[Instruction Profile]
- mode: phase-bound implementation
- source: Phase UI-0 Visual DirectionおよびPhase UI-1 WPFデザイン基盤
- delivery: Codex implementation prompt
- repair scope: not-applicable

[Task]
- 対象Phase: Phase UI-2
- 作業名: MainWindowの情報設計と視覚レイアウト刷新
- 実装目的:
  - FeatherScribeの日常利用で最も重要な「現在状態」「直近結果」「コピー」「貼り付け」を、迷わず確認・操作できるメインウィンドウへ再設計する。
  - ホットキー一覧、不採用候補、再整形などの補助情報を必要なときだけ確認できる構成にする。
  - Phase UI-1で作成したTheme ResourceDictionaryを使用し、ダークテーマ固定の高品質なWPF UIとして仕上げる。
- 採用設計:
  - MainWindowの既存処理契約とx:Nameを維持しながら、XAMLレイアウトを全面的に再構成する。
  - 主要結果を最大のカードとして中央へ配置する。
  - 貼り付けを最重要操作、コピーを次点操作とする。
  - 不採用候補とホットキーガイドは折りたたみ式にする。
  - 今回はレイアウト、階層、余白、カード、表示状態までを対象とする。
  - 本格的なアニメーションはPhase UI-4で実装する。
- スキル `PhoenixQuant Agent Standard` が利用可能な環境では、その実装・検証規律を使用すること。
- `AGENTS.md` が存在する場合は遵守すること。
- UIデザインの正本は `docs/ui/visual_direction.md` とする。
- サブエージェントを使用してはならない。

[Non-negotiable Boundary]
- 音声録音、文字起こし、LLM整形、Validator、ホットキー登録、クリップボード、貼り付けの挙動を変更しない。
- `DictationController`、Core、Infrastructureを変更しない。
- 既存の公開メソッドや処理イベントを変更しない。
- 次の既存x:Nameを維持すること。
  - StatusText
  - HotkeyHelpText
  - LastResultText
  - RejectedResultText
  - ReformatButton
  - AdoptRejectedButton
  - RecopyButton
  - RepasteButton
- 次のClickハンドラを維持すること。
  - ReformatButton_Click
  - AdoptRejectedButton_Click
  - RecopyButton_Click
  - RepasteButton_Click
- モデル名、エンドポイント、タイムアウト、LLM設定を通常画面へ追加しない。
- 設定画面、モデル選択、テーマ切り替えを追加しない。
- RecordingOverlayは今回変更しない。
- Storyboard、Timer、常時アニメーションは今回追加しない。
- 外部UIフレームワーク、アイコンライブラリ、NuGetパッケージを追加しない。

[AI Work-Report Isolation]
- `artifacts/ai_runs/**` は実装者またはレビュー者の一時作業報告領域であり、FeatherScribe製品の一部ではない。
- production source、tests、docs、config、product scripts、workflowから参照、読込、import、引用、必須化してはならない。
- runtime authority、fixture、test oracle、fallback、最新ファイル探索に使用してはならない。
- 既存の禁止参照を発見した場合は `AI_WORK_REPORT_COUPLING` として報告し、同一作業で除去すること。
- 禁止pathを検査するためのrepository testやSSOTルールを追加してはならない。

[Implementation Steps]

1. Phase UI-1の実機表示を開始前確認する
   - Windows GUIが利用可能な場合、現在のMainWindowを起動して確認する。
   - 確認項目:
     - Theme Resourceが正しく適用される
     - 文字色と背景色が読みやすい
     - ボタンのHover、Pressed、Disabled、Keyboard Focusが識別できる
     - 760×500でラベルが切れない
     - MainWindow起動時にResource参照例外がない
   - GUIが利用できない場合:
     - `ENV_GUI_UNAVAILABLE` としてnot-run ledgerへ記録する。
     - 未確認をPASSと報告しない。

2. MainWindow全体レイアウトを再構成する
   - 推奨サイズ:
     - Width: 800
     - Height: 540
     - MinWidth: 720
     - MinHeight: 480
   - WindowStartupLocationは現行どおり維持する。
   - 標準Windowsタイトルバーは維持する。
   - 独自WindowChromeは追加しない。
   - Root Gridの外周余白は24pxを基本とする。
   - 次の縦構成とする。

   1. アプリヘッダー
   2. 現在状態表示
   3. 直近結果カード
   4. 不採用候補・警告の折りたたみ領域
   5. 主要アクション
   6. 操作ガイドの折りたたみ領域

   - 完了条件:
     - 画面が設定画面のように見えない。
     - 直近結果が最も大きく、最も目立つ。
     - 主要操作が画面下部に安定して配置される。
     - 125%および150% DPIでも構造が破綻しない。

3. ヘッダーを実装する
   - 左側に次を表示する。
     - FeatherScribe
     - ローカル音声入力
   - `FeatherScribe` は `AppTitleTextStyle` を使用する。
   - サブタイトルは `CaptionTextStyle` を使用する。
   - 右側にモデル名や設定値を表示しない。
   - 装飾的なロゴ画像や外部アイコンを追加しない。
   - 羽根の印象は色、余白、細いアクセント線などで控えめに表現してよい。

4. 現在状態表示を再設計する
   - `StatusText` は既存コードビハインドが長い状態メッセージも設定するため、狭い固定幅のピルへ押し込まない。
   - 横幅いっぱいに近い、低いステータスバーまたはステータスカードとして配置する。
   - 推奨構造:
     - 左に小さな状態記号
     - 右にStatusText
   - StatusTextは折り返しを許可する。
   - 長いエラーやフォールバック説明でも画面外へ切れないようにする。
   - 色だけで状態を区別しない。
   - 今回は状態による色変更ロジックを追加しない。
   - 現行のStatusText更新処理をそのまま利用する。

5. 直近結果カードを実装する
   - `ResultCardStyle`を使用する。
   - カード内構造:
     - セクション見出し「直近の結果」
     - 補助説明または状態ラベル
     - LastResultText
     - 空状態メッセージ
   - `LastResultText`は最も大きい表示領域を占める。
   - 読み取り専用、選択可能、折り返し、縦スクロール可能を維持する。
   - 行間と内側余白を十分に確保する。
   - 長文でもカード外へはみ出さない。

   空状態:
   - LastResultText.Textが空の場合のみ、次を表示する。
     - 「ホットキーを押して録音を開始します」
   - すべてのホットキーを空状態へ列挙しない。
   - Converterや新しいNuGetを追加しない。
   - XAMLのElementName BindingとDataTriggerで実装してよい。
   - 空状態は入力結果を覆って操作不能にしない。

6. 不採用候補・警告を折りたたみ領域へ移動する
   - 現在の常時表示GroupBoxを廃止する。
   - 明示的なStyleを持つExpanderまたは折りたたみカードとして実装する。
   - 表示名:
     - 「整形候補・警告」
   - 初期状態は折りたたみとする。
   - 内容:
     - 簡潔な説明
     - RejectedResultText
     - AdoptRejectedButton
   - `RejectedResultText`の読み取り専用、折り返し、スクロールを維持する。
   - `AdoptRejectedButton`はこの領域内へ移動する。
   - x:Name、Click、IsEnabled、ToolTipは維持する。
   - 候補がない場合でも大きな空白を占有しない。
   - 候補が存在する場合は、StatusTextの既存メッセージで存在を認識できる状態を維持する。
   - 自動展開のためだけに複雑な状態管理を追加しない。
   - 自動展開を実装する場合は、MainWindow.xaml.csの表示責務に限定し、ドメイン処理へ波及させない。

7. アクション領域を再設計する
   - 主要操作:
     - RepasteButton
     - RecopyButton
   - 補助操作:
     - ReformatButton
   - `AdoptRejectedButton`は不採用候補領域へ移動する。

   推奨配置:
   - 左側:
     - ReformatButton
   - 右側:
     - RecopyButton
     - RepasteButton

   Style:
   - RepasteButton: PrimaryButtonStyle
   - RecopyButton: SecondaryButtonStyle
   - ReformatButton: GhostButtonStyle
   - AdoptRejectedButton: SecondaryButtonStyle

   要件:
   - RepasteButtonを最も強く表示する。
   - RecopyButtonも常に見つけやすくする。
   - ボタンの順序を日常操作の優先度に合わせる。
   - Widthを固定しすぎず、DPI拡大時に文字切れしない。
   - 720px幅でもボタンが重ならない。
   - 必要ならWrapPanelまたはレスポンシブなGrid構成を使う。
   - Click、IsEnabled、ToolTipは維持する。

8. ホットキーガイドを折りたたみ領域へ移動する
   - `HotkeyHelpText`をメイン画面上部から外す。
   - 画面下部または補助領域に、初期折りたたみのExpanderとして配置する。
   - 表示名:
     - 「操作ガイド」
   - `HotkeyHelpText`のx:Nameを維持する。
   - コンストラクタおよびShowHotkeyReportによるText更新を維持する。
   - 登録失敗情報も展開時に確認できる状態を維持する。
   - モデル名や設定詳細を新たに追加しない。
   - 現行HotkeyHelpTextに含まれる既存情報は削除しない。

9. MainWindow用の追加Styleを実装する
   - 必要に応じて `Themes/Controls.xaml` に次の明示Styleを追加する。
   - 推奨キー:
     - MainStatusBarStyle
     - SectionExpanderStyle
     - ExpanderHeaderTextStyle
     - EmptyStateTextStyle
     - SubtleBadgeStyle
   - 実際に必要なものだけ追加すること。
   - 暗黙Styleは追加しない。
   - Style追加だけのためにUserControlを作成しない。
   - 標準WPFのExpanderがダークテーマから浮かないようにする。
   - ExpanderのFocus、Hover、Expanded状態が判別できるようにする。
   - 激しい回転やアニメーションは実装しない。

10. MainWindow.xaml.csの変更を最小化する
   - 原則として既存メソッドの処理内容を維持する。
   - レイアウト変更に必要な表示制御のみ許可する。
   - 許可例:
     - 不採用候補Expanderの展開・折りたたみ
     - 空状態表示の更新
   - 禁止:
     - PipelineStageの意味変更
     - 文言の全面書き換え
     - LLMや貼り付け処理の変更
     - 新しい設定読込
     - 新しいバックグラウンド処理
   - コードビハインドをMVVMへ全面移行しない。

11. XAML契約テストを追加・拡張する
   - `ThemeResourceTests.cs`を拡張するか、MainWindow専用契約テストを追加する。
   - 新しいテスト用パッケージを追加しない。
   - テスト名にPhase番号を含めない。

   最低限確認すること:
   - 必須x:Nameがすべて残っている。
   - 必須Clickハンドラ名がすべて残っている。
   - RepasteButtonがPrimaryButtonStyleを使用する。
   - RecopyButtonがSecondaryButtonStyleを使用する。
   - ReformatButtonがGhostButtonStyleを使用する。
   - AdoptRejectedButtonがSecondaryButtonStyleを使用する。
   - HotkeyHelpTextが折りたたみ領域内に存在する。
   - RejectedResultTextとAdoptRejectedButtonが同じ警告・候補領域内に存在する。
   - LastResultTextが結果カード内に存在する。
   - 参照するStaticResourceがすべて存在する。
   - MainWindow.xamlがXMLとして読み込める。
   - MainWindow.xaml.csの公開操作メソッドが削除されていない。

12. Visual Directionを同期する
   - `docs/ui/visual_direction.md` に次のセクションを追加する。

   ## 18. MainWindow Implementation Mapping

   - 記載内容:
     - 実装レイアウト
     - 各表示領域の役割
     - 主要操作の優先順位
     - 不採用候補領域
     - 操作ガイド領域
     - 空状態
     - 使用した主要Style
     - Phase UI-3以降へ延期した項目
   - 既存のデザイン方針を削除・弱体化しない。
   - 実装上のx:NameやStyle keyと文書を一致させる。

[Required Artifacts]

- must read:
  - docs/ui/visual_direction.md
  - src/FeatherScribe.App/App.xaml
  - src/FeatherScribe.App/MainWindow.xaml
  - src/FeatherScribe.App/MainWindow.xaml.cs
  - src/FeatherScribe.App/Themes/Colors.xaml
  - src/FeatherScribe.App/Themes/Spacing.xaml
  - src/FeatherScribe.App/Themes/Typography.xaml
  - src/FeatherScribe.App/Themes/Motion.xaml
  - src/FeatherScribe.App/Themes/Controls.xaml
  - src/FeatherScribe.Tests/ThemeResourceTests.cs

- must write:
  - src/FeatherScribe.App/MainWindow.xaml
  - src/FeatherScribe.App/Themes/Controls.xaml
  - src/FeatherScribe.Tests/ThemeResourceTests.cs またはMainWindow用契約テスト
  - docs/ui/visual_direction.md
  - MainWindow.xaml.csは表示制御が本当に必要な場合のみ最小変更

- must not rewrite:
  - src/FeatherScribe.App/RecordingOverlay.xaml
  - src/FeatherScribe.App/RecordingOverlay.xaml.cs
  - src/FeatherScribe.App/DictationController.cs
  - src/FeatherScribe.Core/**
  - src/FeatherScribe.Infrastructure/**
  - config/**
  - prompts/**
  - reports/**
  - docs/work/**

- must produce evidence:
  - XAML変更前後の構成要約
  - 必須x:Name維持証跡
  - 必須Click維持証跡
  - Style適用一覧
  - XAML契約テスト結果
  - GUI目視確認結果またはnot-run ledger
  - build/test/format結果

[Detailed Design Notes]

- MainWindow header
  - 責務: アプリ名と用途を静かに示す。
  - 表示: FeatherScribe / ローカル音声入力。
  - 禁止: 設定値、モデル名、接続状態一覧。

- Status area
  - 責務: 現在処理と操作結果を表示する。
  - 入力契約: 既存StatusText.Text更新。
  - 注意: 長いエラーや説明を許容する。
  - 禁止: 狭い固定ピルによる切り捨て。

- Result card
  - 責務: 現在採用されている文字列を最優先で表示する。
  - 入力契約: LastResultText。
  - 空状態: Textが空のときだけ表示。
  - 禁止: 編集可能化、結果内容の自動変更。

- Rejected candidate area
  - 責務: 通常利用を邪魔せず、必要時に候補を確認・採用できる。
  - 入力契約: RejectedResultText / AdoptRejectedButton。
  - 禁止: 自動採用、Validator判定変更。

- Action area
  - 責務: コピーと貼り付けを明確にする。
  - 優先度: Repaste > Recopy > Reformat。
  - 禁止: 新しい操作、確認ダイアログ、処理順変更。

- Hotkey guide
  - 責務: 必要時だけ操作方法と登録失敗を確認できる。
  - 入力契約: HotkeyHelpText。
  - 禁止: 設定編集UIへの拡張。

[Requirements Summary]
- PhaseUI2-R1: MainWindowを結果中心の情報構造へ再設計する。
- PhaseUI2-R2: StatusTextの長文表示契約を維持しつつ状態領域を改善する。
- PhaseUI2-R3: LastResultTextを最大の結果カードとして表示する。
- PhaseUI2-R4: 不採用候補と手動採用操作を折りたたみ領域へ移動する。
- PhaseUI2-R5: 貼り付け、コピー、再整形の視覚的優先順位を明確にする。
- PhaseUI2-R6: HotkeyHelpTextを折りたたみ操作ガイドへ移動する。
- PhaseUI2-R7: 既存x:Name、イベント、IsEnabled、ToolTip、処理契約を維持する。
- PhaseUI2-R8: MainWindow用の明示Styleと契約テストを追加する。
- PhaseUI2-R9: Visual Directionと実装構成を同期する。
- PhaseUI2-R10: RecordingOverlay、ドメイン処理、設定、モデル処理へ変更を波及させない。

[Field Name Authority]
- x:NameとClickハンドラは現行MainWindow.xamlおよびMainWindow.xaml.csを正本とする。
- Style keyは既存Theme ResourceDictionaryと更新後のVisual Directionを正本とする。
- 同じ意味のalias x:Nameやalias Styleを追加しない。
- 既存x:Nameを新名称へ置換しない。
- 文書、XAML、testsで名称を一致させる。

[Domain Boundary Guard]
- required / not-required: not-required
- reason: WPF presentation layerのレイアウトと視覚階層のみを変更し、ドメイン状態や処理意味を変更しない。
- conceptual events promoted to code?: no
- persisted events added?: no
- new DDD surfaces added?: no
- new Gateway / Repository / Adapter surface added?: no
- status: not-required

[Meaning Context Contract]
- actor: FeatherScribeを日常的に使う単一ユーザー
- purpose: 音声入力結果を素早く確認し、コピーまたは元の入力先へ貼り付ける
- context: WPF MainWindow presentation layer
- terms:
  - 直近の結果: 現在採用され、コピー・貼り付け対象となる文字列
  - 不採用候補: Validatorにより自動採用されなかったが、ユーザーが確認できる整形文字列
  - 手動採用: ユーザーが内容を確認したうえで不採用候補を直近結果へ昇格する操作
  - 再整形: 直近raw結果を再度LLM整形へ送る既存操作
  - 直前の入力先: FeatherScribe起動・操作前にフォーカスされていた外部ウィンドウ
- forbidden_interpretations:
  - 不採用候補をエラーとして自動削除しない
  - 手動採用を自動採用へ変更しない
  - 貼り付け操作を自動実行へ変更しない
  - 直近結果を編集可能なエディタへ変更しない
  - 操作ガイドを設定画面へ拡張しない
- machine-readable artifact:
  - このPhaseでは不要
  - harnessが必須とする場合のみAI work-report領域へ作成し、製品コードやtestsから参照しない

[Authoritative Sources]
- docs/ui/visual_direction.md
- 現行MainWindow.xaml
- 現行MainWindow.xaml.cs
- Phase UI-1 Theme ResourceDictionary
- このprompt内のRequirements SummaryとMeaning Context Contract
- repo外zip、画像、チャット添付物を正本として記載しない。

[Required SSOT Sync]
- `docs/ui/visual_direction.md` にMainWindow Implementation Mappingを追加する。
- XAMLの表示領域、x:Name、Style keyと文書を一致させる。
- READMEへ詳細レイアウトを重複記載しない。
- `docs/roadmap.md`は今回変更不要。

[Forbidden Files / Surfaces]
- RecordingOverlay関連
- DictationController
- Core
- Infrastructure
- config
- prompts
- reports
- docs/work
- 音声、LLM、Validator、Hotkey、Clipboardの責務
- 外部依存追加
- MVVM全面移行
- 独自WindowChrome
- アニメーション実装
- インアプリトースト
- ウィンドウ位置保存

[Execution Environment Notes]
- 実行可能:
  - XAML compile
  - resource contract tests
  - MainWindow XAML contract tests
  - dotnet build
  - dotnet test
  - dotnet format
  - git diff checks
- GUI利用可能時:
  - MainWindowの起動
  - 100%、125%、150% DPI確認
  - ボタンHover、Pressed、Disabled、Focus確認
  - 長文StatusText確認
  - 長文結果確認
  - ウィンドウ最小サイズ確認
- GUI利用不可時:
  - `ENV_GUI_UNAVAILABLE` としてnot-run ledgerへ記録
  - GUI未確認をPASSと書かない
  - Windows実機での確認項目を具体的に列挙する

[Local Work Loop]
1. 現行GUIまたはXAML構造を確認する。
2. MainWindowの静的レイアウトだけを再構成する。
3. 必須x:Nameとイベント契約テストを通す。
4. Expander用Styleを追加する。
5. 空状態と長文表示を確認する。
6. 全既存テストを実行する。
7. 同一failure classで2回連続して収束しない場合、変更を広げず最小再現と原因仮説を報告する。

[Completion Gate]

1. Functional gate
   - 直近結果が最大の表示領域になっている。
   - コピー、貼り付け、再整形を従来どおり操作できる。
   - 不採用候補を確認・手動採用できる。
   - HotkeyHelpTextと登録失敗情報を確認できる。
   - 空状態が表示される。

2. Safety gate
   - 必須x:Name、Click、IsEnabled、ToolTip契約が維持されている。
   - 音声、LLM、Validator、Hotkey、Clipboard処理が変更されていない。
   - RecordingOverlayが変更されていない。
   - 外部依存、設定UI、モデルUIが追加されていない。

3. Visual gate
   - 画面が設定画面のように見えない。
   - 直近結果の視覚的優先度が最も高い。
   - StatusTextの長文が切れない。
   - 720px幅で主要操作が重ならない。
   - 125%、150% DPIで文字切れしない。
   - GUI未確認の場合はnot-runとして明示する。

4. Artifact / evidence gate
   - XAML契約テスト結果がある。
   - 必須x:NameとStyle適用の証跡がある。
   - GUI確認またはnot-run ledgerがある。
   - 変更ファイルが明示されている。

5. SSOT gate
   - Visual Directionへ実装マッピングが追加されている。
   - docs、XAML、testsの名称が一致する。

6. Test / static gate
   - git diff --check
   - dotnet build --no-restore
   - dotnet test --no-restore
   - dotnet format --verify-no-changes --no-restore
   - UTF-8 without BOM / LF
   - 未解決StaticResourceなし
   - 新規PackageReferenceなし

7. Process gate
   - サブエージェント未使用
   - Shellリダイレクトによるファイル作成・編集なし
   - config/appsettings.local.jsonとreports生成物をコミット対象に含めない
   - コミット、push、tagを行わない
   - 実行していない確認をPASSと書かない

1つでも未充足の場合は完了扱いにしないこと。

[In Scope]
- MainWindow.xamlのレイアウト刷新
- MainWindow用明示Style
- 必要最小限のMainWindow.xaml.cs表示制御
- XAML契約テスト
- Visual Direction同期
- GUI目視確認またはnot-run ledger

[Out of Scope]
- RecordingOverlay刷新
- アニメーション
- 音量表示
- トースト
- WindowChrome
- 設定画面
- モデル切り替え
- ホットキー編集
- ウィンドウ位置保存
- Core/Infrastructure変更
- LLM、ASR、Validator変更

[Relevant Files]
- docs/ui/visual_direction.md
- src/FeatherScribe.App/MainWindow.xaml
- src/FeatherScribe.App/MainWindow.xaml.cs
- src/FeatherScribe.App/Themes/Controls.xaml
- src/FeatherScribe.App/Themes/Colors.xaml
- src/FeatherScribe.App/Themes/Spacing.xaml
- src/FeatherScribe.App/Themes/Typography.xaml
- src/FeatherScribe.Tests/ThemeResourceTests.cs
- 必要な場合のみMainWindow用XAML契約テスト

[Strict Constraints]
- ファイル編集はPythonによる直接編集を使用すること。
- Shellリダイレクトによるファイル作成・編集は禁止する。
- UTF-8 without BOM / LFを維持する。
- Phase番号をコード識別子、ファイル名、テスト名、ログ、runtime値、resource keyへ含めない。
- 暗黙Styleを追加しない。
- テストのためだけのproduction APIを追加しない。
- 既存処理をMVVMへ全面移行しない。
- 仕様、実装、テストの片手落ちを禁止する。

[Test Plan]

局所確認:
- MainWindow XAML契約テスト
- ThemeResourceTests
- FeatherScribe.App XAML compile
- 必須x:Name、Click、Style参照確認

完了確認:
- `git diff --check`
- `dotnet build --no-restore`
- `dotnet test --no-restore`
- `dotnet format --verify-no-changes --no-restore`

GUI確認:
- 起動時の全体レイアウト
- 空状態
- 通常結果
- 長文結果
- 不採用候補
- 長文StatusText
- Disabledボタン
- Keyboard Focus
- 720px最小幅
- 100%、125%、150% DPI

GUI実行不能の場合:
- 各項目をnot-runとして列挙する。
- XAML compileで代替したとだけ報告し、視覚確認済みとは書かない。

[Output Format - Required Summary]

1. 変更概要
2. 作成・変更したファイル
3. MainWindowの新レイアウト
4. 各表示領域の役割
5. 主要操作の優先順位
6. 折りたたみ領域の実装
7. 空状態の実装
8. 維持したx:Name・イベント契約
9. Visual Direction更新内容
10. 実行した検証と結果
11. GUI確認結果またはnot-run ledger
12. Completion Gateの充足状況
13. 未完了・懸念点
14. 完了可否
   - すべて満たす場合のみ「完了」
   - 1つでも未充足なら「未完了」

[No-Fake-Completion Rule]
- GUI未確認を視覚的に完成したと報告してはならない。
- build成功だけでMainWindow刷新を完了扱いにしてはならない。
- 必須x:Nameまたはイベント契約が欠落している場合は未完了。
- 長文StatusTextやDPI確認が未実行ならnot-runとして明記する。
- Visual Directionと実装が不一致なら未完了。
- 既存処理の意味を変更した場合は未完了。
- production source、tests、docs、config、scriptsのいずれかが `artifacts/ai_runs/**` に依存する場合は未完了。

コミット、push、tagは行わないでください。
```
