# FeatherScribe Visual Direction

## 1. Product Context

FeatherScribe is a local-first Windows WPF app for personal voice input. The daily flow is:

1. Start recording with a global hotkey.
2. Transcribe audio with whisper.cpp.
3. Optionally format text with a local LLM.
4. Copy the result to the clipboard or paste it into the previous input target.

The app is not intended to become a full productivity suite, model manager, cloud service, or multi-user product. The UI should stay focused on the current dictation result, current processing state, and a small number of recovery actions.

Current UI baseline:

- `MainWindow.xaml`: 760 x 500 centered window, plain WPF controls, 12px outer margin.
- `RecordingOverlay.xaml`: transparent, topmost, non-activating pill overlay with `#DD222222` background, 16x8 padding, 14px bold text.
- `App.xaml`: no active application-level ResourceDictionary.
- Existing status text includes idle, recording, transcribing, formatting, pasting, completed, rejected formatting, failures, copy, paste, and reformat actions.

## 2. Design Goals

- Make the latest result the visual center of the app.
- Make processing state immediately understandable without reading long messages.
- Feel light, quiet, intelligent, and high quality.
- Support long daily use without visual fatigue.
- Keep the UI useful as a work tool, not a decorative showcase.
- Make copy, paste, rejected-candidate review, and reformat recovery easy to discover.
- Preserve local-first trust: no account, cloud, sync, or model-store language.
- Use a fixed dark theme.

## 3. Non-Goals

- Do not change production XAML in Phase UI-0.
- Do not change C# code in Phase UI-0.
- Do not add NuGet packages.
- Do not introduce an external UI framework.
- Do not add a model settings screen.
- Do not add theme switching.
- Do not add a light theme.
- Do not add skins.
- Do not add an animation settings screen.
- Do not introduce an icon library.
- Do not add installer or product-distribution UI.
- Do not change the current audio, transcription, LLM, hotkey, clipboard, or paste behavior.
- Do not change LLM prompts.
- Do not change benchmark logic.

## 4. Design Personality

FeatherScribe should feel like a quiet writing instrument: light, precise, and present only when needed. The visual language may borrow from feather, ink, paper, and soft light, but should avoid literal feather illustrations, ornate decoration, or fantasy styling.

The tone is:

- Calm over loud.
- Crisp over playful.
- Modern over nostalgic.
- Personal over enterprise-heavy.
- Focused over dashboard-like.

Use restrained contrast, carefully spaced controls, and short status language. The UI should communicate confidence without pretending the LLM is always correct.

## 5. Color System

Dark theme is fixed. Use colors as semantic tokens rather than ad hoc values.

| Token | HEX | Usage | Do Not Use For |
| --- | --- | --- | --- |
| WindowBackground | `#101418` | App window background; darkest base layer. | Text, borders, active controls. |
| Surface | `#171D23` | Standard panels and result areas. | Full-screen background when nested surfaces are needed. |
| SurfaceElevated | `#1E262D` | Result card, warning card, popover-like surfaces. | Large page background. |
| SurfaceHover | `#26313A` | Hover state for buttons, list rows, secondary actions. | Permanent surfaces. |
| Border | `#2A343D` | Low-contrast separators and card borders. | Focus rings or errors. |
| BorderStrong | `#3A4853` | Active card border, selected warning area, stronger separation. | Decorative outlines everywhere. |
| PrimaryText | `#E8EEF2` | Main text and result text. | Disabled text. |
| SecondaryText | `#AEBBC5` | Explanatory labels, hotkey summaries, secondary status. | Primary result body. |
| MutedText | `#74828D` | Metadata, captions, low-priority hints on WindowBackground or Surface. | Error messages, important state, disabled button labels, text on Selection badges. |
| DisabledText | `#97A3AD` | Disabled button labels (Phase UI-8, Section 25): 6.60:1 on Surface, 5.95:1 on SurfaceElevated. | Enabled text; never combined with an opacity reduction. |
| Accent | `#7FD8D2` | Primary action emphasis, focus ring, active status accent. | Recording, danger, or warning state. |
| AccentHover | `#95E4DE` | Hover state for primary actions. | Static text. |
| AccentPressed | `#5DBCB7` | Pressed state for primary actions. | Borders or inactive controls. |
| Recording | `#E06A6A` | Recording indicator and gentle recording pulse only. | Normal buttons or links. |
| Success | `#7ACB8A` | Completed state, successful formatting, ready feedback. | Generic positive decoration. |
| Warning | `#E2B76B` | Fallback, rejected candidate, manual review needed. | Error state or recording. |
| Danger | `#E07A7A` | Failed state and destructive/error messaging. | Recording pulse if state is not actually recording. |
| OverlayBackground | `#D91A222A` | Recording overlay pill background with opacity. | Main window cards. |
| Selection | `#28484C` | Text selection and selected inline option. | Persistent panel background. |
| Focus | `#9CEBE6` | Keyboard focus ring and focus glow. | Body text or decorative lines. |
| Shadow | `#000000` | `DropShadowEffect` color of the overlay and the In-App Feedback snackbar; the effect sets the opacity. | Fills, borders, or text. |

Keep accent use sparse. The palette should not become a one-note blue-green interface: state colors must remain semantically distinct.

## 6. Typography

Use Windows-standard fonts only. Do not introduce external font files.

| Token | FontFamily | FontSize | FontWeight | LineHeight | Usage |
| --- | --- | ---: | --- | ---: | --- |
| AppTitle | `Segoe UI, Yu Gothic UI` | 18 | Semibold | 24 | Window title area and app identity. |
| PageTitle | `Segoe UI, Yu Gothic UI` | 16 | Semibold | 22 | Main area title when needed. |
| SectionTitle | `Segoe UI, Yu Gothic UI` | 13 | Semibold | 18 | Card headers such as latest result or rejected candidate. |
| Body | `Segoe UI, Yu Gothic UI` | 13 | Regular | 20 | General explanatory text. |
| ResultText | `Yu Gothic UI, Segoe UI` | 15 | Regular | 24 | Dictation result and rejected candidate body. |
| ButtonText | `Segoe UI, Yu Gothic UI` | 13 | Semibold | 18 | Button labels. |
| Caption | `Segoe UI, Yu Gothic UI` | 11 | Regular | 16 | Hotkeys, timestamps, low-priority hints. |
| Status | `Segoe UI, Yu Gothic UI` | 13 | Semibold | 18 | Status pill and overlay status text. |
| MonospaceOptional | `Consolas` | 12 | Regular | 18 | Optional diagnostics only; not for normal UI. |

Avoid oversized headings. FeatherScribe is a compact utility, not a marketing page. Japanese and Latin text should sit comfortably together; prefer `Yu Gothic UI` for longer Japanese result bodies.

## 7. Spacing System

Use an 8px-based scale. Do not introduce arbitrary spacing values unless required by WPF control constraints.

| Value | Usage |
| ---: | --- |
| 4 | Icon-to-label gap, tiny inline separation. |
| 8 | Compact control internal padding, button gaps, small vertical rhythm. |
| 12 | Small card padding, current baseline compatibility. |
| 16 | Standard card padding, button horizontal padding, overlay internal spacing. |
| 24 | Primary result card padding, main content horizontal padding. |
| 32 | Major section gaps and top/bottom rhythm in the main window. |
| 40 | Large empty-state breathing room. |
| 48 | Reserved for future wide layouts only; avoid in compact windows. |

Main window target:

- Outer content padding: 24.
- Card inner padding: 16 or 24 depending on density.
- Button row gap: 8.
- Main result to actions gap: 16.
- Warning/candidate card gap: 12.

## 8. Corner Radius

| Token | Value | Usage |
| --- | ---: | --- |
| SmallControlRadius | 6 | Text boxes, small inline controls. |
| ButtonRadius | 8 | Standard and secondary buttons. |
| CardRadius | 16 | Result card, rejected candidate card, guide card. |
| OverlayRadius | 999 | Recording overlay pill. |
| PillRadius | 999 | Status pill, small semantic badges. |
| ScrollBarThumbRadius | 3 | MainWindow scrollbar thumb (6 px wide, fully rounded ends; Section 25). |

`OverlayRadius` and `PillRadius` are caps, not literal radii. WPF `Border` turns a radius larger than the element into an ellipse, so pill styles apply half of the element height through `PillCornerRadiusConverter`, limited by the token.

Avoid highly rounded large cards. Large surfaces should feel refined, not toy-like.

## 9. Borders and Shadows

| Token | Value | Usage |
| --- | --- | --- |
| StandardBorder | `1px #2A343D` | Standard card and control borders. |
| ElevatedBorder | `1px #3A4853` | Result card focus, warning/candidate expanded panel. |
| SubtleShadow | `0 8 24 #40000000` | Main elevated cards only. |
| OverlayShadow | `0 10 28 #66000000` | Recording overlay and toast-like elements. |
| FocusRing | `2px #9CEBE6` plus 2px inset/offset | Keyboard focus and active controls. |

Do not rely on shadow alone to separate regions. Pair elevation with a subtle border. Avoid many WPF `DropShadowEffect` instances; use them only for the primary result card, overlay, and transient toast if needed. Do not apply heavy shadows to every button or nested surface.

## 10. Main Window Layout

The main window should look like a compact writing cockpit, not a settings page.

ASCII wireframe:

```text
+----------------------------------------------------------+
| FeatherScribe                              [● 待機中]     |
| ローカル音声入力                                          |
+----------------------------------------------------------+
|                                                          |
|  直近の結果                                              |
|  +----------------------------------------------------+  |
|  | 今日の会議は、まず午前10時からでお願いします。     |  |
|  |                                                    |  |
|  +----------------------------------------------------+  |
|                                                          |
|  [クリップボードにコピー] [直前の入力先へ貼り付け]       |
|                                                          |
|  > 整形候補・警告                                       |
|    整形候補は必要なときだけ展開して確認する              |
|                                                          |
+----------------------------------------------------------+
| [再整形]                               [操作ガイド]      |
+----------------------------------------------------------+
```

Layout requirements:

- Latest result is the largest and most visually important element.
- Copy and paste are the primary visible actions.
- Reformat is available but secondary.
- Rejected formatting candidates and warnings are collapsible.
- Hotkey information must not dominate the main area.
- Model name and detailed settings are not shown by default.
- Do not make the window feel like a configuration panel.
- Empty state should tell the user that hotkeys start recording, without listing every setting.

Suggested window size for the refreshed UI: 760-820px wide and 500-560px tall. It should remain usable at 125% and 150% DPI without clipped labels.

## 11. Recording Overlay Layout

The overlay is a non-activating, topmost status pill. It should not take focus.

### Recording

```text
+----------------------+
| ●  録音中      00:08 |
+----------------------+
```

### Transcribing

```text
+----------------------+
| 文字起こし中   ...   |
+----------------------+
```

### Formatting

```text
+--------------------------+
| 整形中                   |
+--------------------------+
```

### Completed

```text
+--------------+
| ✓ 完了       |
+--------------+
```

### Fallback

```text
+------------------------------+
| △ 未整形の文章を使用しました |
+------------------------------+
```

Overlay rules:

- Do not steal focus.
- Do not block the target app.
- Default position is bottom center of the active screen.
- State color and subtle motion may change by state.
- Completed state fades out automatically.
- Do not show full error text in the overlay.
- Keep it short enough to remain unobtrusive during long sessions.
- Use a pill shape, not a rectangular modal.

## 12. Status Representation

| State | Japanese Label | Color | Symbol | MainWindow | RecordingOverlay | Motion | Auto Dismiss |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Idle | 待機中 | SecondaryText / Border | `●` hollow or muted dot | Status pill in header; empty-state hint. | Hidden. | None. | No. |
| Recording | 録音中 | Recording | `●` | Header pill turns recording color; result remains unchanged. | `● 録音中 00:08`. | Gentle pulse. | No. |
| Transcribing | 文字起こし中 | Accent | `...` | Header pill and small progress text. | `文字起こし中 ...`. | Soft ellipsis fade. | No. |
| Formatting | 整形中 | Accent | `✦` or `...` | Inline status near result; actions stay stable. | `整形中`. | Soft fade/slide. | No. |
| Pasting | 貼り付け中 | Accent | `↵` | Short status text near action row. | Optional short pill. | Fast fade. | Yes. |
| Completed | 完了 | Success | `✓` | Success status; result card emphasized briefly. | `✓ 完了`. | Fast fade + subtle color transition. | Yes. |
| Fallback | 未整形を使用 | Warning | `△` | Warning inline card, the unformatted text remains visible. | `△ 未整形の文章を使用しました`. | Fade in. | Yes for overlay, no for MainWindow warning. |
| Warning | 確認が必要 | Warning | `△` | Collapsible warning/candidate card. | Usually hidden. | Expand/collapse height. | No. |
| Failed | 失敗 | Danger | `!` | Inline error with recovery action if possible. | Short failure pill only if needed. | Fade in, no shake. | Overlay yes, MainWindow no. |

Use both color and text/symbol. Never rely on color alone.

## 13. Motion Principles

Animation exists to communicate state changes and operation results, not to decorate the app.

Shared durations:

| Token | Duration | Usage |
| --- | ---: | --- |
| Fast | 100ms | Button state, focus, quick status change. |
| Normal | 180ms | Card expansion, overlay entrance, result emphasis. |
| Slow | 260ms | Overlay fade-out, warning expansion when needed. |

Allowed motion:

- Fade.
- Short slide of 4px to 12px.
- Scale from 0.97 to 1.0 for transient overlay entrance.
- Color interpolation for status transitions.
- Height change for collapsible candidate/warning areas.
- Gentle recording pulse.

Forbidden motion:

- Large bounce.
- Aggressive rotation.
- Large movement of the entire window.
- Delays that make an operation feel slower.
- Cascading animations across many elements.
- Decorative motion that runs forever.
- Blinking.

Motion must not prevent copy, paste, or recording actions from responding immediately.

## 14. Accessibility and DPI

- Support 100%, 125%, and 150% DPI.
- Do not allow button labels or result text to clip.
- Do not distinguish states by color alone.
- Show keyboard focus clearly using `Focus`.
- Keep button click targets at least 32px high; prefer 36px or more.
- Do not make icon-only controls unless they have accessible names and tooltips.
- Animation must not block operation.
- Full high-contrast mode support is outside Phase UI-0 scope.
- Do not add a Reduced Motion settings screen in this phase.
- Avoid blinking and rapid repeated flashes.
- Main result and rejected candidate text must wrap and scroll predictably.

## 15. Component Inventory

| Component | Purpose | Content | Location | Reuse | UserControl Candidate |
| --- | --- | --- | --- | --- | --- |
| StatusPill | Show current app state compactly. | Symbol, Japanese state label, optional short detail. | MainWindow header, overlay. | High. | Yes, if used in both main and overlay. |
| ResultCard | Display the latest accepted/raw result. | Section title, result text, optional subtle metadata. | MainWindow primary area. | Medium. | No initially; keep in MainWindow unless reused. |
| PrimaryButton | Main action. | Copy or paste label. | Main action row. | High. | No; style resource is enough. |
| SecondaryButton | Supporting action. | Reformat, manual adopt. | Main action/footer row. | High. | No; style resource is enough. |
| GhostButton | Low-emphasis action. | Operation guide, show details. | Footer or collapsed headers. | Medium. | No. |
| IconButton | Compact action if needed later. | Familiar symbol and tooltip. | Candidate card header. | Low. | No for Phase UI-0. |
| RecordingIndicator | Recording state. | Red dot, optional timer. | Overlay and header. | Medium. | Yes if timer/pulse logic becomes shared. |
| ProcessingIndicator | Non-blocking progress. | Ellipsis or small shimmer. | Overlay/status pill. | Medium. | No initially. |
| InlineToast | Short feedback. | Copy complete, paste failed, fallback used. | MainWindow or tray-triggered feedback. | Medium. | Yes if multiple locations use it. |
| CollapsibleWarningCard | Show rejected candidate and warnings without crowding. | Reason, candidate text, manual adopt action. | MainWindow below result. | High. | Yes. |
| EmptyState | Guide first use. | Short hotkey instruction and status. | Main result area before first dictation. | Low. | No. |
| HotkeyGuide | Show essential hotkeys without crowding. | Compact hotkey list, hidden or collapsed by default. | Footer or guide panel. | Medium. | Yes only if it grows. |

Use styles and templates before creating UserControls. Create UserControls only for components with reused behavior or complex visual state.

## 16. Acceptance Criteria

- This document is the SSOT for the FeatherScribe visual direction.
- Dark theme palette is defined with concrete HEX values.
- Typography uses Windows-standard fonts only.
- Spacing follows an 8px-based scale.
- Corner radius values are defined by control type.
- Border, shadow, and focus policies are defined.
- MainWindow wireframe is present.
- RecordingOverlay state wireframes are present.
- Idle, Recording, Transcribing, Formatting, Pasting, Completed, Fallback, Warning, and Failed states are defined.
- Motion durations, allowed motion, and forbidden motion are defined.
- Component inventory is defined with UserControl guidance.
- DPI and accessibility rules are defined.
- Non-Goals explicitly exclude production XAML/C# changes, new dependencies, theme switching, prompt changes, and benchmark changes.
- Future UI implementation phases must use this document before changing visual styling.

## 17. WPF Resource Mapping

Phase UI-1 implements the visual direction through explicit WPF ResourceDictionary files under `src/FeatherScribe.App/Themes/`.

Application merge order:

1. `Themes/Colors.xaml`
2. `Themes/Spacing.xaml`
3. `Themes/Typography.xaml`
4. `Themes/Motion.xaml`
5. `Themes/Controls.xaml`

Color tokens map to paired `Color` and `SolidColorBrush` resources. For example, `WindowBackground` is exposed as `WindowBackgroundColor` and `WindowBackgroundBrush`. The same pattern is used for `Surface`, `SurfaceElevated`, `SurfaceHover`, `Border`, `BorderStrong`, `PrimaryText`, `SecondaryText`, `MutedText`, `DisabledText`, `Accent`, `AccentHover`, `AccentPressed`, `Recording`, `Success`, `Warning`, `Danger`, `OverlayBackground`, `Selection`, and `Focus`. `ShadowColor` is a `Color` only (no brush); it is the color of both `DropShadowEffect` resources.

Spacing and radius tokens are exposed as `Space4`, `Space8`, `Space12`, `Space16`, `Space24`, `Space32`, `Space40`, `Space48`, `Inset4`, `Inset8`, `Inset12`, `Inset16`, `Inset24`, `Inset32`, `ButtonPadding`, `OverlayPadding`, `SmallControlCornerRadius`, `ButtonCornerRadius`, `CardCornerRadius`, `OverlayCornerRadius`, `PillCornerRadius`, `FocusRingThickness`, and `StandardBorderThickness`. Component sizes: `LevelMeterMinBarHeight` and `LevelMeterMaxBarHeight` (Section 24), `ScrollBarSize`, `ScrollBarThumbCornerRadius`, and `OverlayShadowInset` (Section 25).

Typography tokens are exposed as font family resources and explicit `TextBlock` styles: `AppFontFamily`, `ResultFontFamily`, `MonospaceFontFamily`, `AppTitleTextStyle`, `PageTitleTextStyle`, `SectionTitleTextStyle`, `BodyTextStyle`, `ResultTextStyle`, `ButtonTextStyle`, `CaptionTextStyle`, `BadgeTextStyle`, `StatusTextStyle`, and `MonospaceTextStyle`.

Motion tokens are exposed as `MotionFastDuration`, `MotionNormalDuration`, `MotionSlowDuration`, `MotionEaseOut`, and `MotionEaseInOut`.

Reusable control styles are explicit resources: `KeyboardFocusVisualStyle`, `BaseButtonStyle`, `PrimaryButtonStyle`, `SecondaryButtonStyle`, `GhostButtonStyle`, `DangerButtonStyle`, `IconButtonStyle`, `CardBorderStyle`, `ResultCardStyle`, `StatusPillStyle`, `CardGroupBoxStyle`, `ReadOnlyTextBoxStyle`, `ResultTextBoxStyle`, `AppToolTipStyle`, `DarkScrollBarThumbStyle`, `DarkScrollBarPageButtonStyle`, and `DarkScrollBarStyle`.

Theme dictionaries contain keyed resources only. The only implicit styles in production XAML are the two window-scoped styles in `MainWindow.Resources`: `ScrollBar` (based on `DarkScrollBarStyle`) and `ToolTip` (based on `AppToolTipStyle`) (Section 25).

`MainWindow.xaml` and `RecordingOverlay.xaml` consume these resources directly while preserving existing control names, event handlers, enabled states, tooltips, wrapping, scroll settings, and overlay window behavior.

## 18. MainWindow Implementation Mapping

The refreshed `MainWindow.xaml` is organized around the daily voice-input loop: understand the current state, read the latest result, then copy or paste it. It keeps the existing code-behind contract while changing the visual hierarchy.

Implemented layout:

1. App header: `FeatherScribe` and `ローカル音声入力`.
2. Status area: `StatusText` inside `MainStatusBarStyle`, with wrapping enabled for long failure or fallback messages.
3. Latest result card: `LastResultText` inside `ResultCardStyle`, with the largest text area and an empty-state prompt.
4. Rejected candidate and warning area: collapsed `SectionExpanderStyle` region containing `RejectedResultText` and `AdoptRejectedButton`.
5. Action area: `ReformatButton` on the left, with `RecopyButton` and `RepasteButton` grouped on the right.
6. Operation guide: collapsed `SectionExpanderStyle` region containing `HotkeyHelpText`.

Primary action priority:

- `RepasteButton`: primary action, `PrimaryButtonStyle`.
- `RecopyButton`: secondary action, `SecondaryButtonStyle`.
- `ReformatButton`: recovery action, `GhostButtonStyle`.
- `AdoptRejectedButton`: candidate review action, `SecondaryButtonStyle`, placed inside the rejected candidate area.

MainWindow-specific style keys:

- `MainStatusBarStyle`
- `SectionExpanderStyle`
- `ExpanderHeaderTextStyle`
- `EmptyStateTextStyle`
- `SubtleBadgeStyle`

State and empty-state behavior:

- `StatusText` remains the code-behind-owned status surface and can display long messages.
- `LastResultText` remains read-only, selectable, wrapping, and vertically scrollable.
- The latest-result empty state is XAML-only and appears only while `LastResultText.Text` is empty.
- `RejectedResultText` remains read-only, wrapping, and vertically scrollable.
- The rejected-candidate empty state is XAML-only and appears only while `RejectedResultText.Text` is empty.
- Hotkey registration output remains owned by `HotkeyHelpText` and is moved into the operation guide rather than removed.

Window height:

- The page must not scroll at startup or when either expander opens. `MainWindow` uses `SizeToContent="Height"` (width 800, minimum 720 x 480) instead of a fixed height.
- Opening or closing `CandidateExpander` or `OperationGuideExpander` refits the height to the content. A manual resize turns `SizeToContent` off, so it is re-enabled on every expander change; the user's width is kept.
- `MaxHeight` is the work-area height of the window's current monitor, and the window is moved up when its bottom would leave the work area.
- `LastResultText` (max 240) and `RejectedResultText` (max 160) have bounded heights; longer text scrolls inside the text box instead of growing the window.
- `MainContentScrollViewer` stays as a fallback and scrolls only when the content is taller than the work area (small screens or high display scale).

Deferred items:

- DPI and hover/focus visual QA require GUI verification.
- No settings screen, model selector, theme switcher, external icon library, or custom WindowChrome is introduced.

## 19. RecordingOverlay Implementation Mapping

The refreshed `RecordingOverlay` is a non-activating, click-through status pill. It shows only short state feedback and never shows settings, model names, full error details, or transcript body text.

Overlay visual states:

- `Hidden`: overlay is not visible.
- `Recording`: shows `録音中`, a recording-colored indicator, a 4-bar volume-linked level meter (`LevelMeter`, Phase UI-7, Section 24), and elapsed time.
- `Transcribing`: shows `文字起こし中`.
- `Formatting`: shows `整形中` or `貼り付け完了・整形中`.
- `Pasting`: shows `貼り付け中`.
- `Completed`: shows a short completion message and auto-hides.
- `Fallback`: shows `未整形の文章を使用しました` and auto-hides.
- `Warning`: shows a short review-needed message and auto-hides.
- `Failed`: shows `処理に失敗しました` and auto-hides.

Pipeline and controller mapping:

- `PipelineStage.Recording` maps to `Recording` and starts the presentation-layer elapsed timer.
- `PipelineStage.Transcribing` maps to `Transcribing`.
- `PipelineStage.Formatting` maps to `Formatting`.
- `PipelineStage.Outputting` maps to `Pasting`.
- `PipelineStage.Completed` does not decide the final overlay state by itself; the final state comes from `PipelineResult` or `BackgroundFormattingResult`.
- `PipelineStage.Failed` maps to `Failed`.
- `PipelineResult` is evaluated in this order (Phase UI-6): failed result → `Failed`; successful result with failed output → `Warning` (`結果は画面に保持しています`), even when background formatting started, so the overlay never claims a paste that did not happen; `BackgroundFormattingStarted` → persistent `Formatting`, so raw-first paste does not make the overlay disappear while background formatting continues; fallback → `Fallback`; otherwise `Completed`.
- A successful `BackgroundFormattingResult` maps to `Completed`.
- A rejected or discarded background formatting result maps to `Warning`.
- Other background formatting failures map to `Fallback`.

Visual treatment:

- State color uses `RecordingBrush`, `AccentBrush`, `SuccessBrush`, `WarningBrush`, or `DangerBrush`.
- State is communicated with color, short text, and a compact glyph.
- The overlay shell uses `OverlayShellStyle`, `OverlayPrimaryTextStyle`, `OverlayTimerTextStyle`, and `OverlayShadowEffect`.
- `OverlayRoot` has a uniform transparent margin `OverlayShadowInset` (21 DIP, Phase UI-8) so the shadow is not clipped by the window edges. The window stays transparent, click-through, and non-activating; placement works on the visible pill (Section 25).
- Motion uses `MotionNormalDuration`, `MotionEaseOut`, and `MotionEaseInOut`.
- Recording uses a gentle indicator pulse and the volume-linked level meter (Section 24). The meter is visible only in `Recording`; every other state collapses it.
- Transcribing uses three small dots with sequential opacity changes.
- Formatting uses a compact horizontal sweep.
- Pasting uses a short moving arrow/progress glyph.
- Completed, fallback, warning, and failed states are temporary and auto-hide.
- Failed remains visible for about 2200ms before auto-hide.

Timer and focus behavior:

- Recording elapsed time is presentation-only and updates once per second.
- The elapsed timer stops whenever the overlay leaves `Recording`.
- Auto-hide uses a single presentation timer and is reset when a new state arrives.
- Show/hide animations are stopped and reused during state transitions.
- `ShowActivated=False`, `WS_EX_NOACTIVATE`, `WS_EX_TRANSPARENT`, `Focusable=False`, and `IsHitTestVisible=False` keep focus with the target application and allow pointer clicks to pass through.

Deferred items:

- Multi-monitor placement and the shadow inset were implemented in Phase UI-8 (Section 25).
- GUI verification of focus, click-through behavior, animation smoothness, and DPI scaling requires a Windows desktop test pass.

## 20. Motion Implementation Mapping

Phase UI-4 applies short presentation motion to the existing WPF surface without changing dictation, transcription, formatting, validation, hotkey, clipboard, or paste behavior.

Motion resource ownership:

- `Themes/Motion.xaml` owns `MotionFastDuration`, `MotionNormalDuration`, `MotionSlowDuration`, `MotionEaseOut`, `MotionEaseInOut`, `MotionPressScale`, `MotionRevealOffset`, `MotionSubtleOffset`, and `MotionMutedOpacity`.
- XAML templates consume these resources directly for button and expander motion.
- `UiMotion` consumes the same resources for MainWindow display updates.
- `RecordingOverlay` consumes the same resources for shell entrance and hide/recovery animation.

Implemented motion:

- MainWindow initial display: `MainContentRoot` reveals once on `Loaded` with opacity and a short vertical offset.
- Status updates: `StatusText.Text` updates immediately, then receives a subtle fade/translate motion.
- Result updates: `LastResultText.Text` updates immediately, then receives a short reveal motion.
- Rejected candidate updates: `RejectedResultText.Text` updates immediately when a candidate is available, then receives a short reveal motion. The expander is not opened automatically.
- Button press: buttons using `BaseButtonStyle` or derived templates scale with `RenderTransform` only, using `MotionPressScale` and `MotionFastDuration`.
- Expander: `SectionExpanderStyle` rotates the chevron (`ChevronRotate`) from -90 degrees to 0 degrees and reveals content (`ExpandSite`, `ExpandSiteTranslate`) with opacity plus a small translate motion. Layout expand/collapse itself is immediate.
  - The chevron storyboards are owned by the `HeaderSite` ToggleButton template (`IsChecked` trigger), because `ChevronRotate` lives in that nested template's name scope. The Expander template's `IsExpanded` trigger owns only the content motion. A storyboard must never target a name from a nested template; that name cannot be resolved and throws at runtime.
  - The chevron's `RenderTransform` is never replaced by a trigger `Setter`; the named `ChevronRotate` stays in place so the rotation is visible.
- RecordingOverlay shell entrance: the shell entrance (Opacity 0→1, TranslateY `MotionRevealOffset`→0, Scale 0.97→1, `MotionNormalDuration`) runs only when the overlay is shown from a non-visible state.
- RecordingOverlay state update: state changes while visible update text, color, glyph, elapsed timer, and state-specific indicator motion. A shell already at rest (Opacity 1, TranslateY 0, Scale 1) is left untouched, so the whole shell never fades out and in between Recording, Transcribing, Formatting, and Completed.
- RecordingOverlay hide interruption: if a new presentation arrives while hide is in progress, the hide motion is frozen at the currently displayed values, the window is not hidden, and the shell continues from those values back to the visible state with `MotionFastDuration`. The new state is displayed immediately.

Animation stop and race policy:

- MainWindow motion never waits before updating text.
- `UiMotion` stops the previous animation before starting the next one and clears animation clocks after completion. A per-element motion version ensures a superseded animation's completion never clears the clocks of a newer one.
- RecordingOverlay shell motion (`OverlayRoot` opacity, `OverlayTranslate`, `OverlayScale`) has a single owner: `AnimateShell` starts entrance, recovery, and hide motion; `StopShellAnimation` freezes it. A shell motion version ensures a superseded animation's completion never hides or resets a newer presentation.
- Overlay state-specific loop animations are stopped before starting the next state animation and when the overlay hides.
- After hide completes, no shell animation clocks remain.
- Overlay auto-hide and recording timers remain one timer each.
- Animation completion is not used as a business-processing completion condition.

Not implemented in this phase:

- Reduced Motion settings screen.
- Motion settings screen.
- Audio-level animation.
- Large page choreography or decorative persistent animation.
- External animation, icon, or UI libraries.

Deferred to Phase UI-5 and later:

- In-app notifications and a copy-completed toast.
- Audio-level (volume-linked) animation in RecordingOverlay (implemented in Phase UI-7, Section 24).
- Multi-monitor placement optimization for RecordingOverlay (implemented in Phase UI-8, Section 25).
- Scrollbar redesign (a minimal window-scoped dark scrollbar was added in Phase UI-8, Section 25).
- Settings screen, Motion settings, and a Reduced Motion setting.

Verification status:

- Static XAML contract tests cover resource keys, button press motion, expander motion, storyboard target name-scope resolution for every `ControlTemplate`, MainWindow motion hooks, RecordingOverlay shell-entrance conditions, and the single shell-motion owner with its superseded-completion guard.
- The FlaUI MainWindow contract test lives in the separate `src/FeatherScribe.GuiTests` project and is run explicitly with `dotnet test src/FeatherScribe.GuiTests/FeatherScribe.GuiTests.csproj`. It expands both expanders and scrolls at 720x480. When no Windows GUI is available, the GUI test run is recorded as `ENV_GUI_UNAVAILABLE` in the not-run ledger instead of being skipped by the test itself. The `FlaUI.Core` and `FlaUI.UIA3` test-only dependencies were approved as an exception for Phase UI-4 and are referenced only by the GUI test project.
- Runtime motion values were sampled frame by frame against the real theme resources: expander chevron and content motion, button press/release and rapid press bursts (no residual scale, neighbors do not move), rapid `UiMotion` status updates, overlay Recording→Transcribing→Formatting→Completed without a shell fade, overlay hide interruption and recovery, the overlay not taking the foreground window, and no clocks remaining after hide.
- Real-screen verification (screen capture plus real OS mouse/keyboard input, at the verification machine's high-DPI scale (single monitor)) confirmed:
  - MainWindow at 720x480: both expanders open and scroll to the end; no text clipping or overlap.
  - Button hover, mouse press (0.98 then back to 1.0), disabled button not scaling on click, keyboard focus ring, and Space key press with no residual scale after rapid presses. Neighboring buttons do not move.
  - RecordingOverlay in every state (Recording, Transcribing, Formatting, Pasting, raw pasted with background formatting, formatting completed, fallback, warning, failed).
  - Overlay entrance frames, no shell fade across visible state transitions, recovery when hide is interrupted by a new recording, and auto-hide after Completed (about 1.2 s).
  - While the overlay is shown, the foreground window and keyboard focus stay in the target window, real typing reaches it, and a real click on the overlay passes through to the window beneath.
- Not run: DPI at 100%, 125%, and 150% (requires changing the Windows display scale; only one high-DPI scale was available), and subjective smoothness judged by a human eye.
- Findings from the real-screen verification, fixed in Phase UI-4:
  - The overlay and the MainWindow badges rendered as ellipses instead of pills, because WPF `Border` scales an oversized radius (999) proportionally in both directions. `OverlayShellStyle`, `StatusPillStyle`, and `SubtleBadgeStyle` now bind `CornerRadius` to half of `ActualHeight` through `PillCornerRadiusConverter`, capped by `OverlayCornerRadius` or `PillCornerRadius`.
  - `LastResultText` and `RejectedResultText` showed no visible indicator when they received keyboard focus. `ReadOnlyTextBoxStyle` now uses the shared `KeyboardFocusVisualStyle` focus ring.

## 21. App Icon / Visual Identity

FeatherScribe uses design option B as its official app icon.

- Meaning: a light feather (lightweight, quiet writing) above a thin flowing stroke that ends in a dot, expressing speech turning into written text.
- Colors: a dark navy rounded square (about `#143052` at the top to `#031222` at the bottom, with a subtle blue rim), and a feather that shades from white to cyan in the `#7FD8D2` accent family. The stroke is bright cyan. Glow stays subtle.
- One identity everywhere: the exe (`ApplicationIcon`), the MainWindow title bar, the taskbar, Alt+Tab, and the notification area all use the same `Assets/Icons/FeatherScribe.ico`. There is no separate tray icon, no state-specific icon, and no light/dark variant.
- Assets: `src/FeatherScribe.App/Assets/Icons/FeatherScribe-512.png` is the 512x512 master. `FeatherScribe.ico` contains 16, 20, 24, 32, 48, 64, 128, and 256 px frames (32-bit BMP up to 64 px, PNG for 128 and 256 px).
- Small sizes (16 to 32 px) may simplify detail: the motif is drawn about 12% larger, the translucent glow is dropped, the feather silhouette is slightly thickened, and the rim is thinner and dimmer, so the feather stays recognizable. The shape and colors must not change into a different logo.
- The design board is a reference only. Production assets contain only the icon: no titles, captions, or mockups.
- A favicon and web icon sets are out of scope for now.
- The app icon is the only place that uses a literal feather. Section 4's guidance against literal feather illustrations still applies to the in-app UI.

## 22. In-App Feedback Implementation Mapping

Phase UI-5 adds a short snackbar to MainWindow that confirms the result of an operation the user just performed (copy, repaste, reformat, manual adopt). It implements the In-App Feedback item deferred in Section 20. Dictation, transcription, formatting, clipboard, and paste behavior are unchanged.

Roles stay distinct:

- `StatusText`: continuous state only (待機中, 録音中…, 文字起こし中…, 整形中…, 整形完了, ...).
- RecordingOverlay: transient pipeline state while MainWindow is not watched (unchanged).
- Tray notifications: important notices while MainWindow is not watched (unchanged).
- In-App Feedback: only the result of an operation the user just performed in MainWindow. Tray menu actions call the same MainWindow methods, so they may show the same in-window feedback; there is no separate tray feedback.

Feedback catalog (single source: `InAppFeedbackMessages` in `src/FeatherScribe.App/InAppFeedback.cs`):

| Trigger | Kind | Message |
| --- | --- | --- |
| Copy succeeded | Success | `クリップボードにコピーしました` |
| Repaste succeeded (Ctrl+V sent) | Success | `直前の入力先へ貼り付けました` |
| Repaste target not found (copied instead) | Warning | `貼り付け先が見つからないため、クリップボードにコピーしました` |
| Reformat started | Info | `再整形を開始しました` |
| Reformat could not start | Warning | `再整形を開始できませんでした` |
| Rejected candidate adopted | Success | `整形候補を採用しました` |
| Adopt requested but no candidate | Warning | `採用できる整形候補がありません` |
| Copy threw | Error | `コピーできませんでした` |
| Repaste threw | Error | `貼り付けできませんでした` |
| Reformat threw | Error | `再整形を開始できませんでした` |
| Adopt threw | Error | `整形候補を採用できませんでした` |

- Exception text is never shown in the snackbar or `StatusText`; it is written to `Debug.WriteLine` only.
- Copy, repaste, and adopt results no longer overwrite `StatusText`. Reformat started keeps its continuing-state text (`再整形中…（高品質）` / `再整形中…`; wording updated in Phase UI-6, see Section 23). A successful adopt sets the state text `整形候補を採用済み`.

Kind presentation (icon plus text, never color alone):

| Kind | Glyph | Glyph background | Duration |
| --- | --- | --- | ---: |
| Success | `✓` | `SuccessBrush` | 1400ms |
| Info | `i` | `AccentBrush` | 1400ms |
| Warning | `!` | `WarningBrush` | 2000ms |
| Error | `×` | `DangerBrush` | 2200ms |

View:

- The Window's direct child is the `MainWindowRoot` Grid. It holds the unchanged `MainContentScrollViewer` and the `FeedbackHost` Border in the same cell, so the snackbar overlays the content and never pushes layout or changes the `SizeToContent="Height"` measurement while collapsed.
- `FeedbackHost`: bottom-center, bottom margin 16, `MaxWidth` 480, `Panel.ZIndex` 10, `IsHitTestVisible=False`, `Focusable=False`, initially `Collapsed` with `Opacity` 0, `FeedbackTranslate` as its `TranslateTransform`, AutomationId `FeedbackHost`, `LiveSetting=Polite`.
- Content: a 20px `FeedbackGlyphBackground` ellipse with the `FeedbackGlyph` text, and `FeedbackText` (AutomationId `FeedbackText`, wraps, at most two lines through `MaxHeight` 40 with `CharacterEllipsis`).
- Style keys in `Themes/Controls.xaml` (all keyed, no implicit styles): `FeedbackSnackbarStyle` (`SurfaceElevatedBrush`, `BorderStrongBrush`, `ButtonCornerRadius`, `Inset12`), `FeedbackShadowEffect` (lighter than `OverlayShadowEffect`), `FeedbackTextStyle` (based on `BodyTextStyle`, `BlockLineHeight` so two lines are exactly 40), and `FeedbackGlyphTextStyle`.
- Showing feedback never calls `Focus` or `Activate`; the snackbar has no actions.

Behavior model:

- `InAppFeedbackState` is a pure state machine (`Hidden`, `Visible`, `Hiding`) without WPF or timers. `Show` returns `Enter` from Hidden, `UpdateInPlace` from Visible, and `RecoverFromHiding` from Hiding, and always records the new feedback as current. The latest feedback wins; there is no queue or history.
- `Version` increments on every show and every hide start. `CompleteHide(version)` only completes a hide that is still the latest, so a stale hide completion never hides a newer feedback.
- MainWindow owns exactly one feedback `DispatcherTimer`, created once and reused (Stop, Interval, Start) for every show, and stopped in `OnClosing`.

Motion (Phase UI-4 resources from `Themes/Motion.xaml`):

- Enter: Opacity 0→1 and TranslateY `MotionRevealOffset`→0 with `MotionNormalDuration` and `MotionEaseOut`.
- Hide: Opacity 1→0 and TranslateY 0→4 with `MotionNormalDuration` and `MotionEaseInOut`. On completion the host is collapsed only if the hide is still current.
- Update in place: glyph, brush, and text change immediately, the timer restarts, and `FeedbackText` receives `UiMotion.SubtleUpdate`. The entrance is not replayed.
- Recover from hiding: the hide motion is frozen at the displayed values, the host is not collapsed, and it returns to visible with `MotionFastDuration`.
- Feedback motion has a single owner (`AnimateFeedback` / `StopFeedbackMotion`) with a motion version, so a superseded animation's completion never resets a newer presentation.

Verification status:

- Static tests cover the state machine, durations, glyph and brush mapping, exact catalog strings, the `MainWindowRoot` / `FeedbackHost` XAML contract, the keyed snackbar style, and the code-behind contract (no exception text in the UI, catalog constants only, one feedback timer).
- The FlaUI MainWindow test checks that the feedback is not shown at startup and that the window still does not scroll.
- Not verified: visual quality and motion smoothness of the snackbar on a real screen, screen reader announcement, and DPI scaling.

## 23. User-facing Wording and Action Availability

Phase UI-6 (Personal Daily-Use Hardening) gives each concept one word, makes every result action available only when it can actually run, and fixes three paste/tray bugs. Identifiers, log codes, config format, startup visibility, X-button meaning, hotkeys, and models are unchanged.

### Wording glossary

User-facing text only (StatusText, RecordingOverlay, In-App Feedback, tray menu and notifications, MainWindow text). Identifiers and log codes keep their names.

| Concept | Use | Do not use in UI text |
| --- | --- | --- |
| raw transcript | `未整形の文章` (short form `未整形`) | `raw`, `raw transcript`, `raw結果`, `直近raw` |
| formatted result | `整形結果` | |
| rejected formatting candidate | `整形候補` | `不採用候補` |
| manual adopt | `採用` (action label `整形候補を採用`) | `手動採用` |
| previous input target | `直前の入力先` | `FeatherScribeの前に使っていたウィンドウ` |
| clipboard | `クリップボード` | |
| reformat | `再整形` (sentence form `整形し直します`) | `もう一度整形` |
| latest result | `直近の結果` | `直近結果` |
| formatting in progress | `整形中` | `Gemma 4 で整形中`, `文章を整えています` |

- Config keys (`llm.enabled` and others) and model names do not appear in normal UI text. The only exception is the startup tray notices about the settings file and hotkey registration, which intentionally point to `config/appsettings.json`. The glossary test has no other exception.
- The reformat tooltip is `直近の未整形の文章を、バックグラウンドで整形し直します。`; `もう一度整形` is not used anywhere in UI text.
- Mode labels (one helper, used by the status and the operation guide): NoFormat `未整形`, PlainFast `整形・軽量`, PlainQuality `整形・高品質`, Polite `丁寧文`, Bullet `箇条書き`, Memo `メモ`, DevInstruction `開発指示`.

Single source: `UserFacingText` in `src/FeatherScribe.App/UserFacingText.cs` holds the status text, tray menu labels, tray notification titles and bodies, mode labels, the operation guide, and the validator display reasons. In-App Feedback messages stay in `InAppFeedbackMessages` (Section 22).

### StatusText

| Trigger | Text |
| --- | --- |
| Idle (XAML initial) | `待機中` |
| Recording | `録音中…（{mode label}）` |
| Transcribing | `文字起こし中…` |
| Formatting | `整形中…` |
| Outputting | `貼り付け中…` |
| Completed stage | `完了` (the Core note message is not appended) |
| Failed stage / failed result | `失敗しました: {short reason}` |
| Result: paste failed | `貼り付けできませんでした・結果は画面に保持しています` |
| Result: raw pasted, background formatting started | `未整形の文章を貼り付けました・整形中…` |
| Result: formatting fallback | `整形できなかったため、未整形の文章を貼り付けました` |
| Result: otherwise | `完了` |
| Background formatting succeeded | `整形完了・コピーまたは貼り付けできます` |
| Background formatting rejected by the validator | `整形候補があります（{display reason}）・確認して採用できます` |
| Background formatting failed | `整形できませんでした・未整形の文章は貼り付け済みです` |
| Reformat started | `再整形中…（高品質）` for PlainQuality, otherwise `再整形中…` |
| Candidate adopted | `整形候補を採用済み` |

- `{short reason}` is the first line of the error message, trimmed, at most 80 characters followed by `…`; an empty message becomes `不明なエラー`.
- A failed paste is checked before the raw-first state, so the status never says the text was pasted when it was not.

### Tray

- Menu: `画面を表示`, `再整形`, `直近の結果をクリップボードにコピー`, `直近の結果を直前の入力先へ貼り付け`, separator, `終了`. Tooltip `FeatherScribe - ローカル音声入力`. There is no adopt item in the tray.
- Notifications (title / body):
  - failure: `FeatherScribe エラー` / `失敗しました: {short reason}`
  - fallback: `整形できませんでした` / `未整形の文章を貼り付けました。`
  - paste failed: `貼り付けできませんでした` / `結果は画面に保持しています。画面からクリップボードにコピーできます。`
  - background success: `整形完了` / `整形結果は「クリップボードにコピー」または「直前の入力先へ貼り付け」で使えます（自動では置き換えません）。`
  - background rejected: `整形候補があります` / `理由: {display reason}`, a line break, then `未整形の文章は貼り付け済みです。整形候補は画面で確認して採用できます。`
  - background failed: `整形できませんでした` / `未整形の文章は貼り付け済みです。`
  - startup settings and hotkey notices: unchanged.
- At dictation completion the notice is chosen by `UserFacingText.CompletionNotice` with the same priority as StatusText and the overlay: failure → paste failed → fallback. A successful raw-first result (background formatting started) shows no notice at completion; the background result notifies later.

### RecordingOverlay text

States and durations are unchanged. Text: Formatting `整形中`, raw pasted with background formatting `貼り付け完了・整形中`, fallback `未整形の文章を使用しました`, paste failed `結果は画面に保持しています`.

`FromPipelineResult` checks a failed paste before the raw-first and fallback states (order: failed → paste failed `Warning` → background formatting `Formatting` → fallback → completed), matching StatusText, so a raw-first paste that did not happen shows the `Warning` state instead of `貼り付け完了・整形中` (see Section 19).

### MainWindow text

- `ReformatButton`: `再整形`, tooltip `直近の未整形の文章を、バックグラウンドで整形し直します。`
- `AdoptRejectedButton`: `整形候補を採用`, tooltip `確認した整形候補を、直近の結果として採用します。`
- `RepasteButton` tooltip: `直近の結果をクリップボードに入れてから、直前の入力先へ貼り付けます。`
- `RecopyButton` tooltip: `直近の結果をクリップボードに入れます。貼り付けはしません。`
- Candidate description: `自動では採用しなかった整形候補です。確認して、必要な場合だけ採用できます。`
- Names, click handlers, `IsEnabled="False"` defaults, and layout are unchanged.

### Operation guide

Compact, one hotkey per line, a full-width space between the hotkey and the mode label, and no config keys:

```text
キーを押して録音、もう一度押して停止します。
Ctrl+Shift+F8　未整形
Ctrl+Shift+F9　整形・軽量
Ctrl+Shift+F10　整形・高品質
Ctrl+Shift+F11　丁寧文
Ctrl+Shift+F12　箇条書き
Ctrl+Alt+Shift+M　メモ
Ctrl+Alt+Shift+D　開発指示
LLM整形: オン
```

The hotkeys are the configured ones. With LLM formatting disabled the last line is `LLM整形: オフ（どのキーでも未整形で入力します）`. Each hotkey that could not be registered adds one line: `⚠ {hotkey}（{mode label}）は使えません: {reason}`.

### Action availability

One meaning for the MainWindow buttons and the tray menu:

| Action | Available when |
| --- | --- |
| Copy (`RecopyButton`, tray copy) | a latest result exists |
| Repaste (`RepasteButton`, tray paste) | a latest result exists |
| Reformat (`ReformatButton`, tray reformat) | idle, a latest raw transcript exists, its mode is not NoFormat, and LLM formatting is enabled |
| Adopt (`AdoptRejectedButton`) | a rejected formatting candidate exists |

- `DictationController` exposes read-only `CanReformat`, `HasLatestResult`, and `HasRejectedCandidate`, read under its lock. `CanReformat` and `ReformatLast` share one private predicate, so the button can never offer a reformat that would not start.
- `ActionAvailability.From(controller)` builds the record. MainWindow's `RefreshActionAvailability()` sets the four buttons after every stage, result, background result, and action; the XAML defaults stay disabled. The tray menu's `Opening` handler sets its items from `MainWindow.GetActionAvailability()`. `画面を表示` and `終了` are always enabled.

### One entry per action

`CopyLatestAsync`, `PasteLatestToPreviousTargetAsync`, `StartReformat`, and `AdoptCandidate` are the only public entries. The button click handlers and the tray menu both call them. Each catches exceptions, writes them to `Debug.WriteLine`, shows the In-App Feedback error, refreshes availability, and never throws, so no exception can escape a button or tray menu handler.

### Previous input target and paste safety

- `ForegroundWindowTracker` ignores shell surfaces (`Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `NotifyIconOverflowWindow`, `TopLevelWindowForOverflowXamlIsland`, `Progman`, `WorkerW`, `Windows.UI.Core.CoreWindow`, `XamlExplorerHostIslandWindow`). Clicking the tray icon or the taskbar therefore no longer replaces the previous input target.
- `App` owns the single tracker, passes it to `MainWindow` and to the pipeline paste guard, and disposes it on exit.
- `PasteTargetGuardTextOutput` wraps the pipeline output. When the dictation result is pasted while a FeatherScribe window is in the foreground (for example MainWindow was opened during recording), it first restores the previous input target. If that is not possible, the text is only copied to the clipboard and the output is reported as failed (paste-failed notification, overlay warning, status text). Ctrl+V is never sent into FeatherScribe's own window. The explicit repaste action keeps using the unwrapped clipboard output because it restores the target itself.

### Keyboard

The natural tab order already matches the visual order: `StatusText` is not a tab stop, then `LastResultText`, the `CandidateExpander` header, (expanded) `RejectedResultText` and `AdoptRejectedButton`, `ReformatButton`, `RecopyButton`, `RepasteButton`, and the `OperationGuideExpander` header. No `TabIndex` is set. The overlay remains non-activating and click-through.

### Verification status

- Static tests cover the glossary (Japanese string literals in `src/FeatherScribe.App/*.cs` and Japanese attribute values in `*.xaml`), every `UserFacingText` string and mode label, short-reason trimming, the display-reason mapping, `CanReformat` / `ReformatLast` consistency, `ActionAvailability.From`, the shell window classes, the paste guard, and the tray / MainWindow code contracts.
- The FlaUI tests check that the result actions are disabled at startup, that Tab reaches `LastResultText` and then the `CandidateExpander` header, and that the window still does not scroll.
- Not verified: the daily-use manual checklist in `docs/acceptance_test.md` (real target apps, real dictation, tray behavior), visual quality, and screen reader output.

## 24. Volume-Linked Recording Meter

Phase UI-7 adds a small level meter to the `Recording` overlay so the user can see that the microphone is actually receiving their voice (a muted microphone or a wrong device shows a still meter). It is presentation-only: recording, ASR, formatting, the pipeline stages, and MainWindow are unchanged.

Data flow:

```text
NAudioRecorder (WaveInEvent.DataAvailable, BufferMilliseconds = 50)
  -> AudioLevelMeter.ComputeLevel(buffer, bytesRecorded)   pure, 16-bit PCM -> 0..1
  -> event Action<float> AudioLevelChanged                  NAudioRecorder only, not IAudioRecorder
App.xaml.cs (composition root)
  -> keep the latest level, at most one pending Dispatcher.BeginInvoke(Render)
  -> RecordingOverlay.SetAudioLevel(float)                  UI thread
RecordingOverlay
  -> AudioLevelSmoother (EMA) -> 4 bar heights
```

- `IAudioRecorder` and `DictationPipeline.StageChanged` are unchanged. The level never enters Core or the pipeline.
- Only one scalar per buffer leaves the recorder. No samples are copied or published, and no file or log contains audio levels.

Level calculation (`src/FeatherScribe.Infrastructure/AudioLevelMeter.cs`):

- Little-endian 16-bit signed samples; a trailing odd byte is ignored; empty or silent input returns 0.
- `rms` over the samples, `db = 20 * log10(rms / 32768)`, `level = clamp((db - FloorDb) / (CeilingDb - FloorDb), 0, 1)` with `FloorDb = -50` and `CeilingDb = -10`.

Recorder events (`NAudioRecorder.AudioLevelChanged`):

- Raised in the existing `DataAvailable` handler after the file write, outside the `writer` lock. The file write logic is unchanged.
- Level computation and the handlers run inside `try { ... } catch (Exception)`, so a failure never propagates into NAudio's callback; recording and ASR continue and the meter just stays still.
- After recording stops, `0` is raised once from the `finally` block (also guarded).

Update rate and thread safety:

- The event rate is bounded by the 50 ms recording buffer (about 20 updates per second, below 30 fps). No timer, `CompositionTarget.Rendering` hook, or Storyboard drives the meter.
- The audio thread never blocks: `App.OnAudioLevelChanged` does `Volatile.Write` of the latest level and posts `Dispatcher.BeginInvoke(DispatcherPriority.Render, ...)` only when `Interlocked.Exchange(ref _levelUpdatePending, 1) == 0`. The UI callback clears the flag first, then applies the latest level. A busy UI thread therefore receives at most one pending update instead of a queue. `Dispatcher.Invoke` is never used for the level.
- `App.OnExit` unsubscribes `AudioLevelChanged`.

Overlay (`RecordingOverlay.xaml`, `RecordingOverlay.xaml.cs`):

- `LevelMeter` is a horizontal `StackPanel` in the right column, directly before `ElapsedText` (both inside one horizontal `StackPanel`), `Margin="10,0,0,0"`, `Visibility="Collapsed"` by default. Its height is fixed to `LevelMeterMaxBarHeight`, so bar changes never resize the pill.
- Four `Rectangle` bars `LevelBar1`..`LevelBar4`: width 3, `RadiusX`/`RadiusY` 1.5, 2 px spacing, `Fill="{StaticResource RecordingBrush}"`, vertically centered.
- Bar height tokens in `Themes/Spacing.xaml`: `LevelMeterMinBarHeight` = 3, `LevelMeterMaxBarHeight` = 14.
- Bar weights `[0.55, 1.0, 0.8, 0.45]`: `height_i = min + (max - min) * clamp(smoothed * weight_i * 1.25, 0, 1)`.
- `AudioLevelSmoother` (`src/FeatherScribe.App/AudioLevelSmoother.cs`): one EMA step per received level, `value += (target - value) * (target > value ? 0.6 : 0.25)`; input clamped to 0..1. It rises while speaking and settles more slowly while silent.
- `SetAudioLevel` returns immediately unless the overlay is in `Recording`, then sets the four heights directly (no animation clocks).
- The meter is visible only in `Recording` (`ApplyIndicatorVisibility`). Entering or leaving `Recording`, `HideStatus`, and `OnClosed` reset the smoother and the bars to the minimum height immediately, so `Transcribing` and later states never show it and a late level update after recording ends is ignored.
- Overlay states, durations, text, and the non-activating, click-through window styles are unchanged.

Verification status:

- Unit tests cover the level mapping (silence, empty, tiny vs. large amplitude, full scale, odd trailing byte, range) and the smoother (attack faster than release, convergence, clamping, reset).
- Static contract tests cover the recorder event (guarded, outside the lock, 0 after stop), `IAudioRecorder` and Core not knowing the level, the App `BeginInvoke` coalescing and unsubscribe, the Recording-only guard in `SetAudioLevel`, the `LevelMeter` XAML contract, no new timer or Storyboard, and the unchanged overlay state count.
- Not run (requires a human at a real microphone and screen): quiet, normal, and loud speech; silence; stop; and the Recording to Transcribing transition hiding the meter.

## 25. Final UI Quality Mapping

Phase UI-8 (Final UI Quality / Release Polish) fixes measured readability problems, the light default scrollbars, and the overlay monitor on multi-monitor setups, and checks resource hygiene. No feature, wording (`UserFacingText`, `InAppFeedbackMessages`), state semantics, layout, control name, click handler, or `IsEnabled` default changes.

### Contrast tokens

WCAG 2.x contrast ratios, computed from `Themes/Colors.xaml` by `FinalUiQualityContractTests`:

| Text | Background | Before | After | Minimum |
| --- | --- | ---: | ---: | ---: |
| Disabled button label | Surface (disabled fill) | MutedText with `Opacity=0.65`: about 2.6:1 effective (2.56 to 2.63 depending on the parent surface) | DisabledText, no opacity: 6.60:1 | 4.5 |
| Disabled button label | SurfaceElevated | MutedText: 3.88:1 before opacity | DisabledText: 5.95:1 | 4.5 |
| Badge caption (`状態`, `コピー・貼り付け対象`) | Selection | MutedText: 2.51:1 | SecondaryText: 5.05:1 | 4.5 |
| PrimaryText | Surface | 14.51:1 | unchanged | 7 |
| Accent button label (WindowBackground) | Accent / AccentHover / AccentPressed | 11.15 / 12.71 / 8.23:1 | unchanged | 4.5 |
| Warning / Danger text | Surface | 9.07 / 5.85:1 | unchanged | 4.5 |
| Focus ring | Surface / SurfaceElevated | 12.49 / 11.27:1 | unchanged | 3 |
| Scrollbar thumb at rest (non-text) | Surface / WindowBackground | Windows default (light) thumb | MutedText: 4.30 / 4.69:1 | 3 |
| Tooltip text (`AppToolTipStyle`) | SurfaceElevated | Windows default light tooltip | PrimaryText: 13.09:1 | 4.5 |
| Caption (MutedText) | WindowBackground / Surface | 4.69 / 4.30:1 | unchanged | kept muted |

- `DisabledTextColor` / `DisabledTextBrush` = `#97A3AD`: at least 4.5:1 on Surface and SurfaceElevated and clearly dimmer than SecondaryText (8.67:1 on Surface).
- Disabled buttons (`BaseButtonStyle` and `PrimaryButtonStyle` templates, inherited by Secondary, Ghost, Danger, and Icon buttons): Foreground `DisabledTextBrush`, the `Opacity=0.65` setter is removed, and the flat `SurfaceBrush` fill with the low `BorderBrush` outline is kept. Disabled is distinguished by the flat fill and outline plus the text color, never by opacity or by color alone, and stays the last template trigger so hover and press never override it. A disabled primary button loses the accent fill.
- `BadgeTextStyle` (in `Typography.xaml`, based on `CaptionTextStyle`, Foreground `SecondaryTextBrush`) is used for the TextBlocks inside `SubtleBadgeStyle` borders. `CaptionTextStyle` itself stays muted for captions on darker surfaces.
- `ShadowColor` = `#000000` replaces the two literal shadow colors of `OverlayShadowEffect` and `FeedbackShadowEffect`.

### Scrollbar

- `DarkScrollBarStyle` (keyed, `OverridesDefaultStyle`): an 8 px (`ScrollBarSize`) transparent track without arrow buttons. The vertical template uses `PART_Track` with `IsDirectionReversed=True`; an `Orientation=Horizontal` trigger swaps in the same visuals with the height set to `ScrollBarSize`.
- `DarkScrollBarThumbStyle`: a 6 px thumb (1 px inset) with `ScrollBarThumbCornerRadius` 3, `MutedTextBrush` at rest (4.30:1 on Surface, 4.69:1 on WindowBackground, meeting the 3:1 non-text contrast requirement), `SecondaryTextBrush` on hover, `PrimaryTextBrush` while dragging. Not focusable. (The first UI-8 draft used `BorderStrongBrush` at rest, 1.80:1, which was too faint.)
- `DarkScrollBarPageButtonStyle`: the two track halves stay `RepeatButton`s (`ScrollBar.PageUpCommand` / `PageDownCommand`, `PageLeftCommand` / `PageRightCommand`) with a transparent, hit-testable background, so clicking the track still pages. Not focusable.
- Scope: one implicit `<Style TargetType="{x:Type ScrollBar}" BasedOn="{StaticResource DarkScrollBarStyle}"/>` in `MainWindow.Resources`. It covers `MainContentScrollViewer` (fallback scroll on small screens) and the internal scrollbars of `LastResultText` and `RejectedResultText`. RecordingOverlay and tray menus are not affected.
- Thumb drag, mouse wheel, keyboard scrolling (PageUp, PageDown, and arrows in the focused text box or scroll viewer), and track-click paging are unchanged because the ScrollViewer and Track parts are unchanged.
- Not a custom scrollbar framework: no new behavior, no auto-hide, no animation.

### Tooltips

- The MainWindow action-button tooltips used the light Windows default. The existing keyed `AppToolTipStyle` (`SurfaceElevatedBrush` background, `BorderStrongBrush` border, `PrimaryTextBrush` text at 13.09:1, `AppFontFamily` 12, `Inset8` padding) is now applied through one implicit `<Style TargetType="{x:Type ToolTip}" BasedOn="{StaticResource AppToolTipStyle}"/>` in `MainWindow.Resources`. Tooltip texts are unchanged.

### Implicit styles

`MainWindow.Resources` holds exactly two implicit styles, `ScrollBar` and `ToolTip`, both window-scoped and both based on keyed theme styles. They are the only implicit styles in production XAML; theme dictionaries remain keyed-only, and the tests enforce both rules.

### Overlay placement

`RecordingOverlay` appears on the monitor the user is typing on.

- Monitor selection (entrance only): the current foreground window when it does not belong to the FeatherScribe process; otherwise `ForegroundWindowTracker.LastExternalWindow` (read-only accessor; `App` passes its single tracker to the overlay constructor); if neither exists (or the window no longer exists), the monitor under the cursor; finally the primary monitor. `MonitorFromWindow` / `MonitorFromPoint` (`MONITOR_DEFAULTTONEAREST`, primary as the last fallback) and `GetMonitorInfo` give the work area in physical pixels, excluding the taskbar. The pure order is `OverlayPlacement.SelectTargetWindow`.
- Shadow inset: the window is sized to its content, so before UI-8 the `OverlayShadowEffect` (BlurRadius 18, ShadowDepth 3, default direction 315 degrees) was clipped at the window edges. `OverlayRoot` now has a uniform transparent `Margin="{StaticResource OverlayShadowInset}"` of 21 DIP. 21 = BlurRadius 18 + ShadowDepth 3, the smallest whole DIP that contains the blur plus the full shadow offset in any direction (the offset along one axis is 3 x cos 45 degrees = 2.1 DIP, so 20 would clip the bottom-right edge by about 0.1 DIP). It is uniform so the pill stays centered in the window, and it also leaves room for the 6 DIP entrance/hide slide, which was clipped before. The margin area is transparent; with `AllowsTransparency`, `IsHitTestVisible=False`, and `WS_EX_TRANSPARENT` it stays click-through, and `WS_EX_NOACTIVATE` / `ShowActivated=False` are unchanged.
- Position: `OverlayPlacement.BottomCenter(workArea, windowWidthPx, windowHeightPx, marginPx, insetPx)` works on the visible pill: it subtracts the inset (`OverlayRoot.Margin`, converted at the window's DPI) from each side, centers the pill horizontally with its bottom 24 DIP (`OverlayPlacement.BottomMarginDip`) above the work-area bottom, clamps the pill into the work area (a pill wider or taller than the work area aligns to the left or top edge), and returns the window position, which is the pill position minus the inset. The window bottom is therefore 3 DIP (24 - 21) above the work-area bottom, so the shadow room also stays inside the work area. The 4-argument overload is the same placement with no inset.
- DPI: placed twice. Pass 1 measures the window (`UpdateLayout`, `GetWindowRect`) at its current DPI and moves it. If the target monitor has another DPI, the move makes WPF apply that DPI and resize the window. Pass 2 re-measures with `GetWindowRect` and `VisualTreeHelper.GetDpi` and corrects the position; it does not move the window when nothing changed. The same code is correct for per-monitor and system DPI awareness because sizes come from the real window rectangle in the same coordinate space as the work area.
- The window is moved only with `SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE)`. It is never activated or focused, `SetForegroundWindow` is never called, and `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT`, `ShowActivated=False`, and Topmost are unchanged. The overlay does not affect foreground tracking (it belongs to FeatherScribe's process, which the tracker ignores).
- Visible state updates do not re-select the monitor and do not re-run placement. Because the pill is `SizeToContent`, a state whose text has a different width re-centers it on the same bottom-center anchor of the monitor chosen at entrance (`KeepAnchoredOnPlacementMonitor`, same inset-aware math); when the size did not change, the window is not moved. The center of the pill therefore never moves between states, and the pill never jumps to another monitor while visible. A hide interrupted by a new state is treated as visible (no re-selection). This re-centering was reviewed and approved by the architect as the intended reading of "re-place only on entrance".

### Resource hygiene

`FinalUiQualityContractTests` reports, over `App.xaml`, `MainWindow.xaml`, `RecordingOverlay.xaml`, and all theme dictionaries:

- Hard-coded `#RGB` / `#RRGGBB` / `#AARRGGBB` values outside `Colors.xaml`: none (the two shadow colors now use `ShadowColor`).
- Unresolved `StaticResource` keys: none (`MainWindow.xaml`, `RecordingOverlay.xaml`, `Colors.xaml`, `Typography.xaml`, `Controls.xaml`).
- Implicit styles in theme dictionaries: none. Production XAML has exactly two implicit styles, the `MainWindow.Resources` `ScrollBar` and `ToolTip` styles. `AppToolTipStyle` is now referenced and left the unreferenced allow-list.
- Keyed resources never referenced from `src/FeatherScribe.App` (XAML `StaticResource` / `DynamicResource`, or a C# string key): `Space4` to `Space48`, `Inset32`, `FocusRingThickness`, `SmallControlCornerRadius`, `ResultTextStyle`, `ButtonTextStyle`, `MonospaceTextStyle`, `DangerButtonStyle`, `IconButtonStyle`, `StatusPillStyle`, and `CardGroupBoxStyle`. All are public design tokens documented in Section 17 and stay as an exact allow-list in the test; nothing else is unreferenced, so no resource was removed.
- Literal `Padding`, `BorderThickness`, `CornerRadius`, and `Duration` values equal to a token: the expander header `Padding="4"` now uses `Inset4` (same meaning: inner padding). Other literals (for example `StatusPillStyle` `12,5`, `SubtleBadgeStyle` `8,3`, margins, and the focus ring offset) do not equal a token or have a different meaning and are unchanged.

### Motion final QA

No motion was added or changed. Existing contracts still hold (button press scale, expander chevron and content, MainWindow reveal and status updates, overlay entrance and single shell-motion owner, In-App Feedback). The volume meter is the only persistent decoration and runs only in `Recording`.

### Verification status

- Static tests: WCAG ratios (including the scrollbar thumb at rest and the tooltip and snackbar text on their backgrounds), the disabled-button triggers, badge text, the scrollbar style contract, the two implicit window-scoped styles, resource hygiene, the shadow inset (equal to BlurRadius + ShadowDepth, uniform, click-through window unchanged), `OverlayPlacement` math (single monitor, negative and offset monitors, clamping, oversized overlay, DPI margin, shadow inset at 100% and 225%), monitor-selection order, and the non-activating placement code contract.
- FlaUI: the result actions are exposed as disabled, stay on screen and unclipped at 720 width with the candidate area expanded, and the page does not scroll horizontally (or vertically when it fits).
- A runtime probe at the verification machine's high-DPI scale (single monitor) confirmed: the scrollbar template (8 px, transparent track, two page halves, MutedText thumb at rest), paging by track command, thumb drag, line and wheel scrolling; disabled buttons at opacity 1 with `DisabledTextBrush`; a string tooltip opened by hovering renders `SurfaceElevatedBrush` / `PrimaryTextBrush` through the window-scoped implicit style; the visible pill at the exact bottom center, 24 DIP (54 px) above the work-area bottom, with the 21 DIP (47 px) shadow room inside the work area; the rendered overlay has a shadow below the pill and fully transparent outer edges (not clipped); and the foreground window never changes.
- Not run: DPI 125% and 150%, a real multi-monitor setup (including monitors with different DPI), screen reader output, and human final visual approval.

## 26. Selected-text editing

Phase UX-1 adds one action: select text in another app, press the selection-edit hotkey (`hotkeys.editSelection`, default `Ctrl+Shift+F7`), and FeatherScribe replaces the selection with the text transformed by `selectionEdit.mode` (default `Polite`). No recording. It is inert while `llm.enabled` is false. No new window, control, color, or motion: feedback reuses the overlay pill and the tray notification.

### Flow

`SelectionEditService` (App, pure orchestration over `IClipboardAccess`, `ISelectionKeyboard`, `ITextFormatter`, foreground and busy predicates):

1. Guards: another selection edit running or a dictation recording/processing (`DictationController.IsBusy`) → busy; `llm.enabled` false → LLM off; FeatherScribe itself in the foreground → capture failed. Nothing is sent and the clipboard is not touched.
2. Remember the foreground window (target), wait until Ctrl/Shift/Alt/Win are released (`GetAsyncKeyState`, at most 1000 ms), re-check the target, snapshot the clipboard and its sequence number, send Ctrl+C.
3. Poll the sequence number every 20 ms up to `selectionEdit.captureTimeoutMilliseconds` (600 ms). No change = no selection (nothing to restore). After the change, wait 50 ms, then read the Unicode text and the VSCode empty-selection marker in one STA call, and restore the original clipboard right away (only while the sequence still equals the copy's).
4. Empty text or the VSCode marker (`vscode-editor-data` with `"isFromEmptySelection": true`, read as its own format or inside Chromium's web custom data) → capture failed.
5. Format with the preset mode (same formatter, prompts, validator and timeouts as dictation). `UsedFallback` (LLM failure, timeout, validator rejection), an exception, or blank text → not edited.
6. Before replacing: the operation must still be the latest, no dictation may have started, and the foreground must still be the target; otherwise the edited text is put on the clipboard and left there. The check is repeated right before Ctrl+V.
7. Replace: snapshot the clipboard (FUNC-1: right before the write), set the edited text, send Ctrl+V, wait 600 ms, restore the snapshot only while the sequence is still ours. This restore runs regardless of `output.restoreClipboard` because the capture overwrote the clipboard without the user asking.

Every clipboard call is wrapped: a clipboard exception maps to the abort of its stage. The service never throws to its caller.

### Feedback

| Result | Overlay state | Overlay text (`UserFacingText`) | Auto hide | Tray notification |
| --- | --- | --- | --- | --- |
| Formatting (after capture) | Formatting, persistent | `整形中` | no | no |
| Replaced | Completed | `選択範囲を置き換えました` | 1000 ms | no |
| LLM off | Warning | `LLM整形がオフのため、選択テキストの編集は使えません` | 1800 ms | no |
| Busy | Warning | `処理中のため、選択テキストの編集を開始できません` | 1800 ms | no |
| Capture failed | Warning | `選択テキストを取得できませんでした` | 1800 ms | no |
| Edit failed | Warning | `編集できなかったため、選択テキストは変更していません` | 1800 ms | yes |
| Target changed | Warning | `入力先が変わったため置き換えませんでした。編集結果はクリップボードにあります` | 2200 ms | yes |
| Replace failed | Failed | `選択範囲を置き換えできませんでした` | 2200 ms | yes |

- The tray notification (title `選択テキストの編集`) is shown only when the user has something to act on or waited for nothing; frequent, self-explanatory results (no selection, busy, LLM off) stay in the overlay.
- The operation guide gets one line after the seven mode lines: `{hotkey}　選択テキストを編集（{mode label}）`; it is omitted when the hotkey is empty (disabled).
- A registration failure uses the mode wording: `⚠ {hotkey}（選択テキスト編集）は使えません: {reason}` in the MainWindow guide, plus the startup tray notice and `logs/events.log`.

### Privacy and safety

- The selected and edited text is never logged or persisted. `logs/events.log` gets one `selection_edit` entry per request with success, `{status}:{reason code}`, duration, mode and the character count only.
- One selection edit at a time. A second press while one runs shows the busy notice and makes the running one stale, so it does not replace. The service holds no lock that the dictation controller waits on.
- Known limits: a clipboard with no supported format (FUNC-1 rules, e.g. a bitmap above 4096 px) cannot be restored; undo is the target app's own Ctrl+Z; rich text in the selection is replaced by plain text.

### Verification status

- Unit tests (fakes only): guards, capture timeout, empty capture, the VSCode marker, success with restore / no restore, formatter fallback, target changed (before set and right before paste), dictation started, stale second request, paste failure, clipboard exceptions at each stage, settings, the extra hotkey registration, texts, overlay mapping and privacy of the event log and debug output.
- Not run (human only): Notepad, Chrome textarea, ChatGPT and VSCode compatibility (`docs/acceptance_test.md` §4).
