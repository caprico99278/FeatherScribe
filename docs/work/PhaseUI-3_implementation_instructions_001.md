[Instruction Profile]
- mode: phase-bound implementation
- source: Phase UI-0 Visual Direction、Phase UI-1 Theme基盤、Phase UI-2 MainWindow刷新
- delivery: Codex implementation prompt
- repair scope: not-applicable

[Task]
- 対象Phase: Phase UI-3
- 作業名: RecordingOverlay 2.0
- 実装目的:
  - FeatherScribeのメイン画面を見ていなくても、録音、文字起こし、整形、貼り付け、完了、フォールバック、失敗の状態を直感的に確認できるフローティングオーバーレイへ刷新する。
  - フォーカスを奪わず、操作対象アプリを妨げず、短く上質なアニメーションで状態変化を伝える。
  - PlainFastのraw先貼り付け後も、バックグラウンド整形が継続中であることを正しく表示する。
- 採用設計:
  - Presentation層専用の`OverlayVisualState`と純粋な状態マッピングを追加する。
  - `RecordingOverlay`は表示、アニメーション、録音経過時間、自動消去のみを担当する。
  - `App.xaml.cs`はPipeline/ControllerイベントをOverlay状態へマッピングする。
  - VisualStateManagerまたは同等のWPF標準機構を使い、外部アニメーションライブラリは導入しない。
  - 状態の意味、Pipeline、Controller、LLM、貼り付け処理は変更しない。
- スキル `PhoenixQuant Agent Standard` を使用して作業すること。
- `AGENTS.md`が存在する場合は遵守すること。
- `doc/QualityBaseline.md`が存在する場合は品質基準として扱うこと。存在しない場合、このPhaseのためだけに作成してはならない。
- UIデザインの正本は`docs/ui/visual_direction.md`とする。
- サブエージェントを使用してはならない。

[Non-negotiable Boundary]
- Coreの`PipelineStage`、`PipelineResult`、`BackgroundFormattingResult`の意味や構造を変更しない。
- `DictationPipeline`、`DictationController`の状態遷移や並行処理を変更しない。
- raw先貼り付け、自動置換禁止、OperationIdによる競合防止を変更しない。
- Tray通知とMainWindow更新を削除しない。
- オーバーレイに設定、モデル名、エラー詳細、文字起こし本文を表示しない。
- オーバーレイを操作可能なツールバーにしない。
- フォーカスを奪わない。
- マウス操作を遮らない。
- 大きな回転、バウンド、点滅、常時動作する装飾を追加しない。
- MainWindowのレイアウトを変更しない。
- 外部UIライブラリ、アイコンライブラリ、NuGetパッケージを追加しない。

[AI Work-Report Isolation]
- `artifacts/ai_runs/**`は実装者・レビュー者の一時的な作業報告領域であり、FeatherScribe製品の一部ではない。
- production source、tests、docs、config、scripts、workflowから参照、読込、import、引用、必須化してはならない。
- runtime authority、fixture、test oracle、fallback、latest-file locatorとして使用してはならない。
- 既存の禁止参照を発見した場合は`AI_WORK_REPORT_COUPLING`として報告し、同一作業で除去すること。
- 禁止pathを文字列として含むrepository testやSSOT guardを追加してはならない。

[Implementation Steps]

1. Phase UI-2の視覚チェックを開始前に行う
   - Windows GUIが利用可能な場合、MainWindowを起動して以下を確認する。
     - 720×480
     - 100%、125%、150% DPI
     - 両Expanderを同時に展開
     - 長いStatusText
     - 長いLastResultText
     - ボタンのHover、Focus、Disabled
   - Phase UI-2起因の明確なレイアウト破綻がある場合:
     - RecordingOverlay作業を始める前に報告する。
     - 無関係なままPhase UI-3へ持ち越さない。
   - GUI利用不能の場合は`ENV_GUI_UNAVAILABLE`としてnot-run ledgerへ記録する。

2. Overlay表示状態を定義する
   - App層に次の状態を定義する。
     - Hidden
     - Recording
     - Transcribing
     - Formatting
     - Pasting
     - Completed
     - Fallback
     - Warning
     - Failed
   - 推奨ファイル名:
     - `OverlayVisualState.cs`
   - Phase番号を型名、列挙値、ファイル名へ含めない。
   - Coreの`PipelineStage`へUI専用状態を追加しない。

3. 状態マッピングを純粋なPresentationロジックへ分離する
   - 推奨ファイル名:
     - `OverlayPresentationMapper.cs`
   - Pipeline/Controllerイベントから次を決定する。
     - OverlayVisualState
     - 表示文言
     - 録音タイマー表示の有無
     - 一時表示か継続表示か
     - 一時表示時間

   必須マッピング:

   PipelineStage.Recording:
   - Recording
   - 「録音中」
   - 次状態まで継続
   - 録音時間表示あり

   PipelineStage.Transcribing:
   - Transcribing
   - 「文字起こし中」
   - 次状態まで継続

   PipelineStage.Formatting:
   - Formatting
   - 「文章を整えています」
   - 次状態まで継続

   PipelineStage.Outputting:
   - Pasting
   - 「貼り付け中」
   - 次状態まで継続

   PipelineStage.Completed:
   - このイベントだけでは最終表示を確定しない。
   - `controller.Completed`の`PipelineResult`を待つ。

   PipelineStage.Failed:
   - Failed
   - 「処理に失敗しました」
   - 一時表示
   - 例外全文や内部エラーコードは表示しない。

   `PipelineResult`:
   - `Success == false`
     - Failed
     - 「処理に失敗しました」
   - `BackgroundFormattingStarted == true`
     - Formatting
     - 「貼り付け完了・整形中」
     - バックグラウンド整形完了まで継続
   - `UsedFallback == true`
     - Fallback
     - 「未整形の文章を使用しました」
   - `OutputSucceeded == false`
     - Warning
     - 「結果をアプリ内に保持しました」
   - その他の成功
     - Completed
     - 「完了」

   `BackgroundFormattingResult`:
   - `FormattedText != null`
     - Completed
     - 「整形完了」
   - `RejectedText != null`
     - Warning
     - 「整形候補を確認できます」
   - `ErrorMessage`が整形結果破棄を示す
     - Warning
     - 「整形候補を確認できます」
   - その他の失敗
     - Fallback
     - 「未整形の文章を使用しました」

   - マッピング処理ではWindow、Dispatcher、Timer、Storyboardを使用しない。
   - 副作用のない決定論的ロジックとしてテスト可能にする。
   - テストのためだけに不必要なpublic APIを増やさない。

4. RecordingOverlay.xamlを再設計する
   - 既存のWindow契約を維持する。
     - WindowStyle=None
     - AllowsTransparency=True
     - Background=Transparent
     - Topmost=True
     - ShowInTaskbar=False
     - ShowActivated=False
     - SizeToContent=WidthAndHeight
     - ResizeMode=NoResize
   - 追加推奨:
     - UseLayoutRounding=True
     - SnapsToDevicePixels=True
     - Focusable=False

   推奨構造:
   - `OverlayRoot`
   - `OverlayShell`
   - 状態アイコン領域
   - `OverlayText`
   - `ElapsedText`

   レイアウト:
   - ピル型
   - 横方向
   - 左: 状態アイコンまたはインジケーター
   - 中央: 状態文言
   - 右: 録音時のみ経過時間
   - MinWidth: 180前後
   - MaxWidth: 420前後
   - 長文エラーは表示しないため、原則1行
   - 画面下部中央の配置は維持する

5. 状態別の視覚表現を実装する

   Recording:
   - RecordingBrush
   - 赤い小さな円
   - 周囲の穏やかなPulse
   - 経過時間`00:00`
   - Pulse周期は約1.2～1.5秒
   - 点滅表現は禁止

   Transcribing:
   - AccentBrush
   - 3つのドットを順番にフェード
   - 激しい移動や回転は禁止

   Formatting:
   - AccentBrushまたは淡いシアン
   - 小さな光点または短い光線が左右に流れる表現
   - 無限回転スピナーは使用しない

   Pasting:
   - AccentBrush
   - 短い進行ラインまたは矢印表現
   - 処理が即完了してもちらつきに見えないこと

   Completed:
   - SuccessBrush
   - チェック記号またはPath
   - 約900ms表示後に退場アニメーション

   Fallback:
   - WarningBrush
   - 三角または警告記号
   - 「未整形の文章を使用しました」
   - 約1800ms表示

   Warning:
   - WarningBrush
   - 情報・注意記号
   - 約1800ms表示

   Failed:
   - DangerBrush
   - ×またはエラー記号
   - 約2200ms表示
   - エラー詳細はMainWindowまたはTray通知へ委ねる

   - 色だけで状態を表現せず、記号・形状・文言を併用する。
   - 外部アイコンを導入せず、標準文字またはWPF Pathを使用する。

6. 表示・退場アニメーションを実装する
   - Phase UI-1のMotion resourceを使用する。

   表示:
   - Opacity: 0 → 1
   - TranslateY: 8 → 0
   - Scale: 0.97 → 1
   - MotionNormalDurationを基準とする。

   退場:
   - Opacity: 1 → 0
   - TranslateY: 0 → 6
   - MotionNormalDurationを基準とする。

   - VisualStateManagerまたは局所Storyboardで実装する。
   - Window表示そのものを待たせない。
   - 新しい状態が退場アニメーション中に来た場合:
     - 古い退場を中止する。
     - Windowを非表示にせず、新状態へ遷移する。
   - ループAnimationは該当状態を離れた時点で停止する。
   - Window非表示後にAnimationやTimerを動かし続けない。

7. 録音経過時間を実装する
   - 録音開始時に`00:00`から開始する。
   - 1秒間隔で更新する。
   - Recording以外へ遷移したらTimerを停止する。
   - 次回録音開始時に0へ戻す。
   - 音声録音処理やPipelineへ経過時間機能を追加しない。
   - Presentation層の`DispatcherTimer`のみで実装する。
   - Timerは1つだけ保持し、多重起動させない。

8. 一時表示と自動消去を実装する
   - 一時表示用Timerを1つだけ保持する。
   - Completed、Fallback、Warning、Failedで使用する。
   - 新しい状態へ遷移したら既存の自動消去予定を解除する。
   - `Thread.Sleep`を使用しない。
   - fire-and-forgetの`Task.Delay`を乱立させない。
   - DispatcherTimerまたはStoryboard.Completedで制御する。
   - Hide前に退場アニメーションを行う。
   - `Hide()`後はTimerとループAnimationが停止していること。

9. フォーカス非取得とクリック透過を強化する
   - ShowActivated=Falseを維持する。
   - Overlay内の要素は操作対象にしない。
   - `IsHitTestVisible=False`を適切に設定する。
   - 可能なら標準Win32拡張スタイルを使用し、次を設定する。
     - WS_EX_NOACTIVATE
     - WS_EX_TRANSPARENT
   - 実装はRecordingOverlayのPresentation責務に閉じる。
   - 外部Interopパッケージを追加しない。
   - オーバーレイ表示時に現在の入力先フォーカスが変わらないことをGUIで確認する。
   - オーバーレイ位置のマルチモニター最適化はPhase UI-8へ延期する。

10. App.xaml.csのイベント連携を更新する
   - 現在の`ShowStatus(string, bool)`中心の連携を、新しい状態APIへ置き換える。
   - Tray通知とMainWindow更新は維持する。

   必須フロー:
   - StageChanged:
     - Recording / Transcribing / Formatting / Outputtingを即時反映
     - Completedは最終表示を確定しない
     - Failedは安全な短文で表示
   - controller.Completed:
     - PipelineResultをMapperへ渡して表示を決定
     - BackgroundFormattingStartedの場合はOverlayを消さず「貼り付け完了・整形中」を継続
   - BackgroundFormattingCompleted:
     - 成功、候補不採用、失敗をMapperで判定
     - 最終状態を一時表示してから自動消去

   - 内部例外全文をOverlayへ渡さない。
   - 既存Tray通知文とMainWindow更新処理を削除しない。
   - staleなBackgroundFormattingResultは既存Controller側OperationId判定へ委ね、別の競合ロジックを重複追加しない。

11. Overlay専用StyleとEffectを追加する
   - 必要に応じて`Themes/Controls.xaml`へ明示Resourceを追加する。

   推奨キー:
   - OverlayShellStyle
   - OverlayPrimaryTextStyle
   - OverlayTimerTextStyle
   - OverlayShadowEffect

   - DropShadowEffectを使用する場合:
     - OverlayShellの1箇所に限定
     - BlurRadiusを過大にしない
     - ShadowDepthを小さくする
     - 常時複数箇所へ適用しない
   - 暗黙Styleは追加しない。

12. テストを追加する
   - 推奨:
     - `OverlayPresentationMapperTests.cs`
     - `RecordingOverlayResourceTests.cs`または既存ThemeResourceTestsの拡張
   - テスト名、型名、ファイル名へPhase番号を含めない。

   Mapperテスト:
   - Recording → 録音中、継続、タイマー表示
   - Transcribing → 文字起こし中
   - Formatting → 文章を整えています
   - Outputting → 貼り付け中
   - Stage Completedだけでは最終表示を決めない
   - Pipeline失敗 → Failed
   - BackgroundFormattingStarted → 貼り付け完了・整形中
   - UsedFallback → Fallback
   - OutputSucceeded=false → Warning
   - 通常成功 → Completed
   - Background整形成功 → 整形完了
   - RejectedTextあり → Warning
   - Background整形失敗 → Fallback
   - 一時表示時間が状態別に正しい

   XAML契約テスト:
   - OverlayRoot、OverlayShell、OverlayText、ElapsedTextが存在
   - 必須VisualStateが存在
   - Recording、Transcribing、Formatting、Pasting用インジケーターが存在
   - Completed、Fallback、Warning、Failedの記号が存在
   - Motion resourceを参照している
   - WindowStyle、AllowsTransparency、Topmost、ShowInTaskbar、ShowActivatedを維持
   - 未解決StaticResourceがない
   - 外部画像・外部フォント・外部アイコン参照がない

   - WPF Windowを実際に生成する不安定なテストを無理に追加しない。
   - 状態決定は純粋Mapperで検証し、XAML構造は契約テストで検証する。

13. Visual Directionを同期する
   - `docs/ui/visual_direction.md`に次を追加する。

   `## 19. RecordingOverlay Implementation Mapping`

   記載内容:
   - OverlayVisualState一覧
   - Pipeline/Controllerイベントとの対応
   - 状態別文言、色、記号
   - 録音タイマー
   - 表示・退場アニメーション
   - 一時表示時間
   - raw先貼り付け後のバックグラウンド整形表示
   - フォーカス非取得・クリック透過
   - Phase UI-4以降へ延期した内容

   - docs、code、testsの状態名とResource keyを一致させる。
   - MainWindow実装マッピングを変更・削除しない。

[Required Artifacts]

- must read:
  - docs/ui/visual_direction.md
  - src/FeatherScribe.App/App.xaml.cs
  - src/FeatherScribe.App/RecordingOverlay.xaml
  - src/FeatherScribe.App/RecordingOverlay.xaml.cs
  - src/FeatherScribe.App/Themes/Colors.xaml
  - src/FeatherScribe.App/Themes/Spacing.xaml
  - src/FeatherScribe.App/Themes/Typography.xaml
  - src/FeatherScribe.App/Themes/Motion.xaml
  - src/FeatherScribe.App/Themes/Controls.xaml
  - src/FeatherScribe.Core/DictationPipeline.cs
  - src/FeatherScribe.App/DictationController.cs
  - src/FeatherScribe.Tests/ThemeResourceTests.cs

- must write:
  - src/FeatherScribe.App/RecordingOverlay.xaml
  - src/FeatherScribe.App/RecordingOverlay.xaml.cs
  - src/FeatherScribe.App/App.xaml.cs
  - src/FeatherScribe.App/OverlayVisualState.cs
  - src/FeatherScribe.App/OverlayPresentationMapper.cs
  - src/FeatherScribe.App/Themes/Controls.xaml
  - mapperおよびXAML契約テスト
  - docs/ui/visual_direction.md
  - AssemblyInfo.csはinternalテスト公開が必要な場合のみ最小変更

- must not rewrite:
  - src/FeatherScribe.App/MainWindow.xaml
  - src/FeatherScribe.App/MainWindow.xaml.cs
  - src/FeatherScribe.App/DictationController.cs
  - src/FeatherScribe.Core/**
  - src/FeatherScribe.Infrastructure/**
  - config/**
  - prompts/**
  - reports/**
  - docs/work/**

- must produce evidence:
  - 状態マッピング表
  - VisualState一覧
  - Animation/Timer lifecycle説明
  - フォーカス非取得・クリック透過の実装根拠
  - Mapperテスト結果
  - XAML契約テスト結果
  - GUI確認またはnot-run ledger
  - build/test/format結果

[Requirements Summary]
- PhaseUI3-R1: RecordingOverlayを状態別の高品質なピル型UIへ刷新する。
- PhaseUI3-R2: UI専用状態と決定論的なイベントマッピングを実装する。
- PhaseUI3-R3: 録音、文字起こし、整形、貼り付けを視覚的に区別する。
- PhaseUI3-R4: 完了、フォールバック、警告、失敗を一時表示して自動消去する。
- PhaseUI3-R5: raw先貼り付け後のバックグラウンド整形中状態を正しく維持する。
- PhaseUI3-R6: 録音時間をPresentation層だけで表示する。
- PhaseUI3-R7: フォーカスを奪わず、マウス操作を遮らない。
- PhaseUI3-R8: AnimationとTimerを状態遷移時に確実に停止・再利用する。
- PhaseUI3-R9: Tray通知、MainWindow更新、Pipeline意味、OperationId競合防止を維持する。
- PhaseUI3-R10: Visual Direction、code、testsを同期する。

[Field Name Authority]
- Core状態名は既存`PipelineStage`を正本とする。
- UI状態名は`OverlayVisualState`を正本とする。
- 同じ意味のalias状態を追加しない。
- XAMLのVisualState名、Mapper、docs、testsで名称を一致させる。
- `PipelineStage.Completed`をそのまま`OverlayVisualState.Completed`へ単純変換しない。
- 最終表示は`PipelineResult`または`BackgroundFormattingResult`から決定する。

[Domain Boundary Guard]
- required / not-required: required
- reason:
  - Pipeline完了とバックグラウンド整形完了は異なる状態であり、UI表示が処理状態を誤って昇格・完了扱いしない必要がある。
- checked high-risk terms:
  - Completed
  - Formatting
  - Fallback
  - Failed
  - BackgroundFormattingStarted
- forbidden:
  - raw貼り付け完了をバックグラウンド整形完了と表示しない
  - 整形候補不採用をパイプライン失敗と表示しない
  - Output失敗時に結果そのものが失われたと表示しない
  - stale結果を新しい操作の状態として表示しない
- persisted events added?: no
- new Gateway / Repository / Adapter added?: no
- status: must-pass

[Meaning Context Contract]
- actor: 他アプリへ音声入力している単一ユーザー
- purpose: メイン画面を見ずに、現在の処理段階と結果を把握する
- context: WPF transient overlay presentation
- terms:
  - Completed: 対象処理が正常に完了し、追加のバックグラウンド処理が残っていない状態
  - Formatting: LLM整形が実行中である状態。raw貼り付け済みの場合も含む
  - Fallback: 整形は採用されなかったが、raw文字列は利用可能または貼り付け済みの状態
  - Warning: 結果は保持されているが、ユーザー確認が必要な状態
  - Failed: パイプラインの主処理が成功しなかった状態
- forbidden_interpretations:
  - Fallbackを全面的な失敗として赤表示しない
  - Warningを正常完了として緑表示しない
  - BackgroundFormattingStartedをCompletedとして即時消去しない
  - 内部エラー全文をオーバーレイへ表示しない
- machine-readable artifact:
  - implementer harnessが必須要求する場合のみAI work-report領域へ作成する。
  - production code、tests、docsからAI work-reportを参照しない。

[Authoritative Sources]
- docs/ui/visual_direction.md
- 現行`PipelineStage`
- 現行`PipelineResult`
- 現行`BackgroundFormattingResult`
- 現行`App.xaml.cs`イベント購読
- このprompt内のRequirements Summary、Field Name Authority、Meaning Context Contract
- repo外zip、画像、チャット添付物を正本として記載しない。

[Required SSOT Sync]
- `docs/ui/visual_direction.md`へRecordingOverlay Implementation Mappingを追加する。
- Overlay状態名、表示文言、色、Animation、表示時間を同期する。
- READMEへ詳細仕様を重複記載しない。
- `docs/roadmap.md`は今回変更不要。

[Forbidden Files / Surfaces]
- MainWindow
- DictationController
- Core
- Infrastructure
- config
- prompts
- reports
- docs/work
- 音声処理
- LLM処理
- Validator
- Clipboard処理
- モデル設定
- 新規外部依存
- マルチモニター最適化
- 音量連動表示

[Execution Environment Notes]
- 実行可能:
  - XAML compile
  - Mapper unit tests
  - XAML/resource contract tests
  - dotnet build
  - dotnet test
  - dotnet format
  - git diff checks
- GUI利用可能時:
  - 状態別表示
  - Animation
  - 自動消去
  - フォーカス維持
  - クリック透過
  - 録音タイマー
  - 100%、125%、150% DPI
  - 新状態が退場中に来るケース
- GUI利用不能時:
  - `ENV_GUI_UNAVAILABLE`としてnot-run ledgerへ記録する。
  - 視覚、フォーカス、クリック透過、実時間AnimationをPASSと書かない。
  - 実機で必要な確認項目を具体的に列挙する。

[Local Work Loop]
1. 純粋Mapperと単体テストを先に実装する。
2. Overlayの静的レイアウトを実装する。
3. VisualStateとAnimationを1状態ずつ追加する。
4. Timer lifecycleを実装する。
5. App.xaml.csとの連携を置き換える。
6. raw先貼り付け・バックグラウンド整形フローをテストする。
7. 全回帰テストを実行する。
8. 同一failure classで2回連続して収束しない場合、変更範囲を広げず最小再現と原因仮説を報告する。

[Completion Gate]

1. Functional gate
   - 全必須状態を表示できる。
   - 録音時間が表示される。
   - raw先貼り付け後は整形中表示が継続する。
   - BackgroundFormattingCompleted後に正しい最終状態を表示する。
   - 一時状態は所定時間後に消える。

2. Safety gate
   - フォーカスを奪わない。
   - マウス操作を妨げない。
   - Tray通知とMainWindow更新を維持する。
   - Core、Controller、Pipeline、OperationId処理を変更しない。
   - stale結果を表示しない。
   - TimerとAnimationが多重化・残留しない。

3. Visual gate
   - 状態を色だけでなく形と文言で区別できる。
   - 録音、文字起こし、整形、貼り付けの動きが異なる。
   - 点滅、大回転、過剰な動きがない。
   - 表示・退場時にちらつかない。
   - DPI拡大時に文字やピルが切れない。
   - GUI未確認の場合はnot-runとして明記する。

4. Artifact / evidence gate
   - Mapperテスト結果がある。
   - XAML契約テスト結果がある。
   - 状態遷移表がある。
   - Timer/Animation停止根拠がある。
   - GUI確認またはnot-run ledgerがある。

5. SSOT gate
   - Visual Directionへ実装マッピングが追加されている。
   - docs、code、XAML、testsの状態名が一致する。

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
   - 実行していない検証をPASSと書かない

1つでも未充足なら完了扱いにしないこと。

[In Scope]
- RecordingOverlay.xaml
- RecordingOverlay.xaml.cs
- App.xaml.csのOverlayイベント連携
- UI専用状態とMapper
- Overlay専用Style、Effect、Animation
- 録音経過時間
- 自動消去
- フォーカス非取得とクリック透過
- Mapper/XAML契約テスト
- Visual Direction同期

[Out of Scope]
- MainWindow変更
- 音量連動アニメーション
- マルチモニター最適化
- インアプリトースト
- 設定画面
- モデル切り替え
- Core/Infrastructure変更
- Pipeline/Controller変更
- LLM/ASR/Validator変更

[Strict Constraints]
- ファイル編集はPythonによる直接編集を使用すること。
- Shellリダイレクトによるファイル作成・編集は禁止する。
- UTF-8 without BOM / LFを維持する。
- Phase番号をコード識別子、ファイル名、テスト名、ログ、runtime値、Resource keyへ含めない。
- 暗黙Styleを追加しない。
- `Thread.Sleep`を使用しない。
- TimerやStoryboardを状態ごとに無制限生成しない。
- テストのためだけの公開APIを追加しない。
- 仕様、実装、テストの片手落ちを禁止する。

[Test Plan]

局所:
- OverlayPresentationMapperTests
- RecordingOverlay XAML契約テスト
- ThemeResourceTests
- App.xaml.csイベントマッピング確認
- FeatherScribe.App XAML compile

回帰:
- `git diff --check`
- `dotnet build --no-restore`
- `dotnet test --no-restore`
- `dotnet format --verify-no-changes --no-restore`

GUI:
- 録音開始とタイマー
- 文字起こし
- 整形
- raw貼り付け完了・バックグラウンド整形中
- 貼り付け
- 完了
- フォールバック
- 警告
- 失敗
- 退場中に新録音開始
- フォーカス維持
- クリック透過
- 100%、125%、150% DPI

[Output Format - Required Summary]
1. 変更概要
2. 作成・変更したファイル
3. Overlay状態一覧
4. Pipeline/Controllerイベントとの対応
5. 状態別の見た目とAnimation
6. 録音タイマーと自動消去
7. フォーカス非取得・クリック透過
8. raw先貼り付け後の表示フロー
9. 追加したテスト
10. Visual Direction更新内容
11. 実行した検証と結果
12. GUI確認結果またはnot-run ledger
13. Completion Gate充足状況
14. 未完了・懸念点
15. 完了可否
   - 全Gateを満たす場合のみ「完了」
   - 1つでも未充足なら「未完了」

[No-Fake-Completion Rule]
- GUI未確認を視覚的に完成したと報告してはならない。
- Mapperテスト成功だけでAnimation lifecycleを完了扱いにしてはならない。
- raw先貼り付け後にOverlayが消える場合は未完了。
- TimerまたはループAnimationが非表示後も動作する場合は未完了。
- フォーカスまたはマウス操作を妨げる場合は未完了。
- Stage Completedだけで最終成功表示を確定する実装は未完了。
- Visual Directionと実装が不一致なら未完了。
- production source、tests、docs、config、scriptsのいずれかが`artifacts/ai_runs/**`へ依存する場合は未完了。

コミット、push、tagは行わないでください。
