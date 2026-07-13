[Instruction Profile]
- mode: phase-bound implementation
- source: Phase UI-0で確定したFeatherScribe Visual Direction
- delivery: Codex implementation prompt
- repair scope: not-applicable

[Task]
- 対象Phase: Phase UI-1
- 作業名: WPFデザイン基盤の構築
- 実装目的:
  - Phase UI-0で確定した色、文字、余白、角丸、コントロール、モーションの基準をWPF ResourceDictionaryとして実装する。
  - MainWindowとRecordingOverlayが、共通デザイントークンを利用できる状態にする。
  - 後続のPhase UI-2以降で、色やサイズを各XAMLへ直接記述せずにUIを改善できる基盤を作る。
- 採用設計:
  - App.xamlから複数のResourceDictionaryを決められた順序で読み込む。
  - ダークテーマ固定とし、テーマ切り替え機構は作らない。
  - 外部UIフレームワークやアイコンライブラリは導入しない。
  - 明示的なx:Keyを持つスタイルを基本とし、影響範囲の広い暗黙Styleは原則追加しない。
  - 現行レイアウト、x:Name、イベントハンドラ、処理フローは維持する。
- スキル `PhoenixQuant Agent Standard` が利用可能な環境では、その実装・検証規律を使用すること。
- `AGENTS.md` が存在する場合は遵守すること。
- 本FeatherScribeリポジトリでは、Phase UI-1の品質・デザイン正本は `docs/ui/visual_direction.md` とする。
- 存在しない `doc/QualityBaseline.md` を、このPhaseのためだけに新規作成してはならない。
- サブエージェントを利用してはならない。ユーザーが明示的に再許可した場合のみ例外とする。

[Non-negotiable Boundary]
- 今回はデザイン基盤の実装に限定する。
- MainWindowの画面構成や情報配置を再設計しない。レイアウト刷新はPhase UI-2で行う。
- RecordingOverlayの状態別アニメーションや構造変更は行わない。刷新はPhase UI-3で行う。
- 音声録音、文字起こし、LLM整形、Validator、ホットキー、クリップボード、貼り付け処理を変更しない。
- ボタンのx:Name、Click、IsEnabled、ToolTipを変更しない。
- MainWindow.xaml.cs、DictationController、Core、Infrastructureの動作を変更しない。
- モデル選択、設定画面、テーマ切り替え、ライトテーマを追加しない。
- 新しいNuGetパッケージを追加しない。
- 現行機能の挙動が変わった場合は未完了とする。

[AI Work-Report Isolation]
- `artifacts/ai_runs/**` は実装者またはレビュー者の一時的な作業報告領域であり、FeatherScribe製品の一部ではない。
- production source、tests、docs、config、product scripts、workflowから、このpathまたは配下fileを参照、読込、import、引用、必須化してはならない。
- runtime authority、fixture、test oracle、golden file、fallback、latest-file locatorとして使用してはならない。
- 既存の禁止参照を発見した場合は `AI_WORK_REPORT_COUPLING` として報告し、この作業で除去すること。
- 禁止pathを文字列として含むrepository testやSSOT guardを追加してはならない。

[Implementation Steps]

1. ResourceDictionary構成を追加する
   - 以下のファイルを作成する。

   src/FeatherScribe.App/Themes/Colors.xaml
   src/FeatherScribe.App/Themes/Spacing.xaml
   src/FeatherScribe.App/Themes/Typography.xaml
   src/FeatherScribe.App/Themes/Motion.xaml
   src/FeatherScribe.App/Themes/Controls.xaml

   - 完了条件:
     - 各ファイルが単一責務になっている。
     - UTF-8 without BOM / LFで保存されている。
     - XAMLコンパイルが成功する。

2. App.xamlへテーマ辞書を統合する
   - Application.ResourcesをResourceDictionaryへ変更する。
   - 以下の順序でMergedDictionariesへ追加する。

   1. Themes/Colors.xaml
   2. Themes/Spacing.xaml
   3. Themes/Typography.xaml
   4. Themes/Motion.xaml
   5. Themes/Controls.xaml

   - Controls.xamlは他の辞書に依存するため最後に読み込む。
   - 完了条件:
     - アプリ起動時にすべてのリソースを参照できる。
     - 重複キーや未解決StaticResourceがない。

3. Colors.xamlを実装する
   - `docs/ui/visual_direction.md` のColor Systemを正本とする。
   - 各色についてColorとSolidColorBrushを定義する。

   必須Colorキー:
   - WindowBackgroundColor
   - SurfaceColor
   - SurfaceElevatedColor
   - SurfaceHoverColor
   - BorderColor
   - BorderStrongColor
   - PrimaryTextColor
   - SecondaryTextColor
   - MutedTextColor
   - AccentColor
   - AccentHoverColor
   - AccentPressedColor
   - RecordingColor
   - SuccessColor
   - WarningColor
   - DangerColor
   - OverlayBackgroundColor
   - SelectionColor
   - FocusColor

   必須Brushキー:
   - WindowBackgroundBrush
   - SurfaceBrush
   - SurfaceElevatedBrush
   - SurfaceHoverBrush
   - BorderBrush
   - BorderStrongBrush
   - PrimaryTextBrush
   - SecondaryTextBrush
   - MutedTextBrush
   - AccentBrush
   - AccentHoverBrush
   - AccentPressedBrush
   - RecordingBrush
   - SuccessBrush
   - WarningBrush
   - DangerBrush
   - OverlayBackgroundBrush
   - SelectionBrush
   - FocusBrush

   - 色値は `docs/ui/visual_direction.md` のHEX値と一致させる。
   - Brushへ別のHEX値を直接重複記述せず、対応するColor resourceを参照する。

4. Spacing.xamlを実装する
   - 8px基準の値を共通化する。

   必須Doubleキー:
   - Space4
   - Space8
   - Space12
   - Space16
   - Space24
   - Space32
   - Space40
   - Space48

   必須Thicknessキー:
   - Inset4
   - Inset8
   - Inset12
   - Inset16
   - Inset24
   - Inset32
   - ButtonPadding
   - OverlayPadding
   - FocusRingThickness
   - StandardBorderThickness

   必須CornerRadiusキー:
   - SmallControlCornerRadius
   - ButtonCornerRadius
   - CardCornerRadius
   - OverlayCornerRadius
   - PillCornerRadius

   - ButtonPaddingは現在のボタン行が760px幅で破綻しない値にする。
   - OverlayPaddingはPhase UI-0の16px横、8px縦を基準にする。
   - 任意の数値を各画面へ追加するのではなく、原則としてこの辞書の値を使用する。

5. Typography.xamlを実装する
   - 外部フォントを追加しない。
   - Windows標準フォントのみを使用する。

   必須FontFamilyキー:
   - AppFontFamily
   - ResultFontFamily
   - MonospaceFontFamily

   必須Styleキー:
   - AppTitleTextStyle
   - PageTitleTextStyle
   - SectionTitleTextStyle
   - BodyTextStyle
   - ResultTextStyle
   - ButtonTextStyle
   - CaptionTextStyle
   - StatusTextStyle
   - MonospaceTextStyle

   - FontSize、FontWeight、LineHeightは `docs/ui/visual_direction.md` に合わせる。
   - Styleは明示的x:Keyを持たせる。
   - TextBlock全体へ影響する暗黙Styleは追加しない。

6. Motion.xamlを実装する
   - 今回はモーション値とEasingのみ定義し、Storyboardは実装しない。

   必須キー:
   - MotionFastDuration: 100ms
   - MotionNormalDuration: 180ms
   - MotionSlowDuration: 260ms
   - MotionEaseOut
   - MotionEaseInOut

   - MotionEaseOutは終了時に自然に減速するEasingとする。
   - MotionEaseInOutは展開・折りたたみ等で使用できる穏やかなEasingとする。
   - 常時実行されるAnimation、Timer、Storyboardは追加しない。

7. Controls.xamlを実装する
   - 外部UIライブラリを使わず、標準WPFのStyleとControlTemplateで構築する。

   必須Styleキー:
   - KeyboardFocusVisualStyle
   - BaseButtonStyle
   - PrimaryButtonStyle
   - SecondaryButtonStyle
   - GhostButtonStyle
   - DangerButtonStyle
   - IconButtonStyle
   - CardBorderStyle
   - ResultCardStyle
   - StatusPillStyle
   - CardGroupBoxStyle
   - ReadOnlyTextBoxStyle
   - ResultTextBoxStyle
   - AppToolTipStyle

   BaseButtonStyle要件:
   - 最低32px以上、推奨36pxのクリック高
   - ButtonTextStyle相当の文字表現
   - ButtonCornerRadiusを使用
   - Disabled、Hover、Pressed、Keyboard Focusの状態が区別できる
   - 色だけでなく境界線またはFocus ringでも状態を示す
   - 押下時にレイアウトがずれない
   - Animationはまだ追加しない

   PrimaryButtonStyle:
   - AccentBrushを主背景とする
   - HoverはAccentHoverBrush
   - PressedはAccentPressedBrush
   - 文字色とのコントラストを確保する

   SecondaryButtonStyle:
   - SurfaceElevatedBrushとBorderStrongBrushを使用する
   - Primaryより視覚的優先度を下げる

   GhostButtonStyle:
   - 通常時は低い視覚的優先度
   - Hover、Focus時のみ背景または境界線を明確にする

   DangerButtonStyle:
   - 危険操作専用として定義する
   - 今回の画面へ無理に適用しない

   IconButtonStyle:
   - 最低クリック領域を確保する
   - 今回はアイコンライブラリを導入しない
   - 未使用でも後続Phase用の基礎Styleとして定義してよい

   CardGroupBoxStyle:
   - 現行GroupBoxをカード風に見せるための互換Styleとする
   - Header、Content、Borderを標準WPFで表示する
   - MainWindowのレイアウトや表示内容を変更しない

   ResultTextBoxStyle:
   - 読み取り専用
   - TextWrapping有効
   - 縦スクロール可能
   - Surface系背景
   - PrimaryTextBrush
   - テキスト選択可能
   - 選択色はSelectionBrushを使用

8. 現行XAMLへ共通リソースを最小適用する
   - MainWindow.xaml:
     - WindowBackgroundBrush
     - PrimaryTextBrush
     - AppFontFamily
     - StatusTextStyle
     - CaptionTextStyle
     - CardGroupBoxStyle
     - ResultTextBoxStyle
     - PrimaryButtonStyle
     - SecondaryButtonStyle
     - GhostButtonStyle
   - 推奨ボタン割当:
     - RepasteButton: PrimaryButtonStyle
     - RecopyButton: SecondaryButtonStyle
     - AdoptRejectedButton: SecondaryButtonStyle
     - ReformatButton: GhostButtonStyle
   - RecordingOverlay.xaml:
     - OverlayBackgroundBrush
     - OverlayCornerRadius
     - OverlayPadding
     - StatusTextStyle

   - 次の要素は維持する:
     - Windowサイズ
     - Grid構成
     - RowDefinition
     - x:Name
     - Clickイベント
     - IsEnabled
     - ToolTip
     - TextWrapping
     - Scroll設定
     - WindowStyle
     - AllowsTransparency
     - Topmost
     - ShowActivated
     - SizeToContent

   - RecordingOverlay.xaml.csに存在する状態別背景色制御は今回変更しない。
   - Overlayの状態別デザイン刷新はPhase UI-3へ延期する。

9. Resource contract testを追加する
   - 既存の `src/FeatherScribe.Tests` を利用する。
   - 新しいテスト用NuGetパッケージを追加しない。
   - 例として `ThemeResourceTests.cs` を追加してよい。
   - テスト名やコード識別子にPhase番号を含めない。

   最低限確認すること:
   - 各ResourceDictionaryがロードできる。
   - 主要Color、Brush、Style、Durationキーが存在する。
   - WindowBackgroundColor、AccentColor、PrimaryTextColor等の代表値がVisual Directionと一致する。
   - App.xamlで辞書が依存順にマージされている。
   - MainWindow.xamlとRecordingOverlay.xamlが参照するリソースキーが存在する。
   - テストのためだけにproduction codeへ公開APIを追加しない。

10. SSOTを同期する
   - `docs/ui/visual_direction.md` に次のセクションを追加する。

   ## 17. WPF Resource Mapping

   - 以下を簡潔に記載する:
     - Themeファイル構成
     - App.xamlの読み込み順
     - Color tokenからColor/Brush resource keyへの命名規則
     - Typography Style key
     - Control Style key
     - Motion resource key
     - 明示Styleを基本とし、暗黙Styleを避ける方針

   - デザイン方針そのものを書き換えない。
   - 実装と文書のキー名を完全に一致させる。

[Required Artifacts]

- must read:
  - docs/ui/visual_direction.md
  - src/FeatherScribe.App/App.xaml
  - src/FeatherScribe.App/MainWindow.xaml
  - src/FeatherScribe.App/RecordingOverlay.xaml
  - src/FeatherScribe.App/FeatherScribe.App.csproj
  - src/FeatherScribe.Tests/FeatherScribe.Tests.csproj

- must write:
  - src/FeatherScribe.App/Themes/Colors.xaml
  - src/FeatherScribe.App/Themes/Spacing.xaml
  - src/FeatherScribe.App/Themes/Typography.xaml
  - src/FeatherScribe.App/Themes/Motion.xaml
  - src/FeatherScribe.App/Themes/Controls.xaml
  - src/FeatherScribe.App/App.xaml
  - src/FeatherScribe.App/MainWindow.xaml
  - src/FeatherScribe.App/RecordingOverlay.xaml
  - src/FeatherScribe.Tests/ThemeResourceTests.cs
  - docs/ui/visual_direction.md

- must not rewrite:
  - config/appsettings.json
  - config/appsettings.local.json
  - config/profiles.json
  - prompts/**
  - reports/**
  - src/FeatherScribe.Core/**
  - src/FeatherScribe.Infrastructure/**
  - src/FeatherScribe.App/MainWindow.xaml.cs
  - src/FeatherScribe.App/RecordingOverlay.xaml.cs
  - src/FeatherScribe.App/DictationController.cs
  - docs/work/**

- must produce evidence:
  - 変更ファイル一覧
  - resource key一覧
  - XAML build結果
  - unit test結果
  - format結果
  - 未実行項目がある場合のnot-run ledger

[Detailed Design Notes]

- Colors.xaml
  - 責務: Visual Directionの色をWPF Color/Brushへ変換する。
  - 入力契約: HEX値はVisual Directionを正本とする。
  - 失敗時挙動: キー欠落、値不一致、重複キーはCompletion blocker。
  - テスト観測項目: 代表Color値と必須キー。

- Spacing.xaml
  - 責務: 余白、Padding、BorderThickness、CornerRadiusを共通化する。
  - 入力契約: 8px基準の定義を維持する。
  - 失敗時挙動: 既存760pxレイアウトが破綻する場合は未完了。
  - テスト観測項目: 必須キーの存在。

- Typography.xaml
  - 責務: 表示用途別の文字Styleを提供する。
  - 入力契約: Windows標準フォントのみ。
  - 失敗時挙動: 外部フォント依存または暗黙Style追加は未完了。
  - テスト観測項目: 必須FontFamilyとStyle。

- Motion.xaml
  - 責務: 後続Phase用のDurationとEasingを提供する。
  - 入力契約: 100ms、180ms、260ms。
  - 失敗時挙動: Storyboardや常時Animationを追加した場合は範囲外。
  - テスト観測項目: Duration値とEasing resource。

- Controls.xaml
  - 責務: 明示的な再利用Styleを提供する。
  - 入力契約: Colors、Spacing、Typography、Motionを参照する。
  - 失敗時挙動: 未解決resource、暗黙の全Control override、操作不能は未完了。
  - テスト観測項目: Style keyとTargetType。

[Requirements Summary]
- PhaseUI1-R1: Visual Directionに基づく5つのTheme ResourceDictionaryを追加する。
- PhaseUI1-R2: App.xamlでTheme辞書を依存順に読み込む。
- PhaseUI1-R3: 色、文字、余白、角丸、モーション値、コントロールStyleを共通化する。
- PhaseUI1-R4: 現行MainWindowとRecordingOverlayへ共通リソースを最小適用する。
- PhaseUI1-R5: 現行のレイアウト、操作、x:Name、イベント、処理フローを維持する。
- PhaseUI1-R6: 必須resource keyを自動検証するテストを追加する。
- PhaseUI1-R7: `docs/ui/visual_direction.md` と実装resource keyを同期する。
- PhaseUI1-R8: 新規依存、設定UI、テーマ切り替え、機能変更を導入しない。

[Field Name Authority]
- Resource keyの正本は、この指示と更新後の `docs/ui/visual_direction.md` とする。
- ColorとBrushはそれぞれ `...Color`、`...Brush` の命名規則を使用する。
- Styleは `...Style`、Durationは `...Duration` とする。
- 同じ意味に複数のalias resourceを作らない。
- 実装中に命名衝突が判明した場合、別名fallbackを追加せず報告する。
- XAML、docs、testsでresource keyを完全一致させる。

[Domain Boundary Guard]
- required / not-required: not-required
- reason: UI視覚基盤のみであり、業務状態、承認、実行、永続化、外部接続の意味を変更しない。
- conceptual events promoted to code?: no
- persisted events added?: no
- new DDD surfaces added?: no
- new Gateway / Repository / Adapter surface added?: no
- status: not-required

[Meaning Context Contract]
- actor: FeatherScribeを日常利用する単一ユーザー
- purpose: 音声入力結果と処理状態を、疲労なく直感的に確認・操作できるUI基盤を作る
- context: WPF presentation layer
- terms:
  - Surface: 背景上に配置されるカードまたは操作領域
  - Accent: 主要操作、フォーカス、処理中状態に限定して使う強調色
  - PrimaryButton: その時点で最も重要な1操作
  - SecondaryButton: 主要操作を補助する操作
  - GhostButton: 常時強調する必要がない低優先度操作
  - Motion: 状態変化と操作結果を伝える短い視覚的フィードバック
- forbidden_interpretations:
  - デザイン基盤を設定画面やテーマエンジンへ拡張しない
  - Accentをすべての操作へ使用しない
  - Motionを装飾目的の常時Animationとして使用しない
  - UI Style追加を業務処理変更の理由にしない
- machine-readable artifact:
  - このPhaseでは不要
  - 実装環境のharnessが必須要求する場合のみAI work-report領域へ作成し、製品コードやtestsから参照しない

[Authoritative Sources]
- docs/ui/visual_direction.md
- このprompt内のRequirements SummaryとField Name Authority
- 現行App.xaml、MainWindow.xaml、RecordingOverlay.xaml
- 既存テストとプロジェクト設定
- repo外ファイル名、zip、画像、チャット添付物を正本として記載しないこと。

[Required SSOT Sync]
- `docs/ui/visual_direction.md` にWPF Resource Mappingを追加する。
- 実装resource keyと文書resource keyを同一作業で一致させる。
- READMEへテーマファイル一覧を長く追記しない。
- 既にVisual Directionへの導線があるため、READMEの重複変更は不要。

[Forbidden Files / Surfaces]
- config/**
- prompts/**
- reports/**
- docs/work/**
- Core、Infrastructure、音声・LLM・Validator・Hotkey・Clipboard責務
- MainWindow.xaml.cs
- RecordingOverlay.xaml.cs
- 外部依存追加
- 広域リファクタ
- Phase UI-2以降のレイアウト刷新
- Phase UI-3以降のアニメーション実装

[Execution Environment Notes]
- この実行環境で実行可能なもの:
  - XAML compile
  - dotnet build
  - dotnet test
  - dotnet format
  - git diff checks
  - resource contract tests
- GUI表示が利用できる場合:
  - MainWindow起動確認
  - ボタンのHover、Pressed、Disabled、Focus確認
  - 760×500での表示確認
- GUI表示が利用できない場合:
  - ENV_GUI_UNAVAILABLEとしてnot-run ledgerへ記録する
  - GUI未確認をPASSと記載しない
  - XAML compile、resource tests、既存回帰が成功していれば、このPhaseのコード完了判定は可能
  - 視覚的な最終確認はPhase UI-2開始前にWindows実機で行う

[Local Work Loop]
1. Theme辞書を1つずつ作成し、XAML compileを確認する。
2. App.xamlで統合し、未解決resourceを除去する。
3. Controls.xamlを追加し、ThemeResourceTestsを通す。
4. MainWindowとRecordingOverlayへ最小適用する。
5. 既存105件を含む全テストを実行する。
6. 同一failure classで2回連続して収束しない場合、変更範囲を広げず、最小再現と原因を報告する。

[Completion Gate]

1. Functional gate
   - 5つのTheme ResourceDictionaryが存在する。
   - App.xamlから正しい順序でロードされる。
   - MainWindowとRecordingOverlayが共通resourceを使用する。
   - 既存操作が維持される。

2. Safety gate
   - 音声、LLM、Validator、Hotkey、Clipboard処理に変更がない。
   - x:Name、イベント、操作契約に変更がない。
   - 外部依存とテーマ切り替えが追加されていない。
   - Phase UI-2以降を先取りしていない。

3. Artifact / evidence gate
   - 必須resource key一覧を報告する。
   - resource contract testの結果を報告する。
   - GUI未実行の場合はnot-run理由を記録する。

4. SSOT gate
   - Visual DirectionへWPF Resource Mappingが追加されている。
   - docs、XAML、testsのresource keyが一致する。

5. Test / static gate
   - git diff --check
   - dotnet build --no-restore
   - dotnet test --no-restore
   - dotnet format --verify-no-changes --no-restore
   - BOM、CRLF、未解決StaticResourceの確認

6. Process gate
   - サブエージェント未使用
   - UTF-8 without BOM / LF
   - Shellリダイレクトによるファイル作成・編集なし
   - config/appsettings.local.jsonとreports生成物をコミット対象に含めない
   - コミット、push、tagを行わない
   - 実行していない検証をPASSと書かない

Completion Gateを1つでも満たせない場合は、完了扱いにしないこと。

[In Scope]
- WPF Theme ResourceDictionary
- App.xaml resource integration
- MainWindow.xamlへの最小Style適用
- RecordingOverlay.xamlへの最小Style適用
- Theme resource contract tests
- Visual Directionのresource mapping同期

[Out of Scope]
- MainWindowのレイアウト再設計
- Warning領域の折りたたみ実装
- 新しいUserControl
- 録音タイマー
- 音量バー
- Storyboard
- 状態遷移アニメーション
- トースト通知
- ウィンドウ位置保存
- 設定UI
- モデル切り替えUI
- ライトテーマ
- アイコンライブラリ
- C#業務動作変更

[Relevant Files]
- docs/ui/visual_direction.md
- src/FeatherScribe.App/App.xaml
- src/FeatherScribe.App/MainWindow.xaml
- src/FeatherScribe.App/RecordingOverlay.xaml
- src/FeatherScribe.App/FeatherScribe.App.csproj
- src/FeatherScribe.App/Themes/*.xaml
- src/FeatherScribe.Tests/FeatherScribe.Tests.csproj
- src/FeatherScribe.Tests/ThemeResourceTests.cs

[Strict Constraints]
- ファイル編集はPythonによる直接編集を使用すること。
- Shellリダイレクトによるファイル作成・編集は禁止する。
- UTF-8 without BOM / LFを維持する。
- Phase番号をコード識別子、resource key、ファイル名、テスト名、ログ、runtime値へ含めない。
- 既存テスト基盤を再利用する。
- テストのためだけのproduction APIを追加しない。
- 影響範囲の広い暗黙Styleを導入しない。
- 仕様、実装、テストの片手落ちを禁止する。

[Test Plan]

局所確認:
- ThemeResourceTests
- FeatherScribe.AppのXAML compile
- MainWindow.xamlとRecordingOverlay.xamlのresource解決

完了確認:
- `git diff --check`
- `dotnet build --no-restore`
- `dotnet test --no-restore`
- `dotnet format --verify-no-changes --no-restore`

追加静的確認:
- 新規・変更テキストファイルのUTF-8 without BOM / LF
- `StaticResource` / `DynamicResource` の未解決参照がないこと
- `config/appsettings.local.json` がgit statusへ出ていないこと
- `reports/*.md` / `reports/*.csv` がコミット候補へ入っていないこと
- 新規PackageReferenceがないこと

[Output Format - Required Summary]

最終報告は次の順で返すこと。

1. 変更概要
2. 作成・変更したファイル
3. Theme ResourceDictionaryの構成
4. 主要resource key一覧
5. MainWindowへ適用したStyle
6. RecordingOverlayへ適用したStyle
7. Visual Directionの更新内容
8. 実行した検証と結果
9. 未実行の検証と理由
10. Completion Gateの充足状況
11. 未完了・懸念点
12. 完了可否
   - すべてのCompletion Gateを満たす場合のみ「完了」
   - 1つでも未充足なら「未完了」

[No-Fake-Completion Rule]
- 実行していない検証をPASSと書いてはならない。
- build成功だけでUI resource契約を完了扱いにしてはならない。
- resource testのみ成功しても既存回帰が未実行なら完了ではない。
- GUI表示を確認していない場合は、その事実を明記する。
- SSOTとresource keyが不一致なら完了ではない。
- 既存機能またはイベント契約を変更した場合は完了ではない。
- production source、tests、docs、config、scriptsのいずれかが `artifacts/ai_runs/**` に依存する場合は完了ではない。

コミット、push、tagは行わないでください。
