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
| MutedText | `#74828D` | Disabled controls, metadata, low-priority hints. | Error messages or important state. |
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
|    不採用候補は必要なときだけ展開して確認する            |
|                                                          |
+----------------------------------------------------------+
| [もう一度整形]                         [操作ガイド]      |
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
| 文章を整えています       |
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
| Formatting | 整形中 | Accent | `✦` or `...` | Inline status near result; actions stay stable. | `文章を整えています`. | Soft fade/slide. | No. |
| Pasting | 貼り付け中 | Accent | `↵` | Short status text near action row. | Optional short pill. | Fast fade. | Yes. |
| Completed | 完了 | Success | `✓` | Success status; result card emphasized briefly. | `✓ 完了`. | Fast fade + subtle color transition. | Yes. |
| Fallback | 未整形を使用 | Warning | `△` | Warning inline card, raw remains visible. | `△ 未整形の文章を使用しました`. | Fade in. | Yes for overlay, no for MainWindow warning. |
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

Color tokens map to paired `Color` and `SolidColorBrush` resources. For example, `WindowBackground` is exposed as `WindowBackgroundColor` and `WindowBackgroundBrush`. The same pattern is used for `Surface`, `SurfaceElevated`, `SurfaceHover`, `Border`, `BorderStrong`, `PrimaryText`, `SecondaryText`, `MutedText`, `Accent`, `AccentHover`, `AccentPressed`, `Recording`, `Success`, `Warning`, `Danger`, `OverlayBackground`, `Selection`, and `Focus`.

Spacing and radius tokens are exposed as `Space4`, `Space8`, `Space12`, `Space16`, `Space24`, `Space32`, `Space40`, `Space48`, `Inset4`, `Inset8`, `Inset12`, `Inset16`, `Inset24`, `Inset32`, `ButtonPadding`, `OverlayPadding`, `SmallControlCornerRadius`, `ButtonCornerRadius`, `CardCornerRadius`, `OverlayCornerRadius`, `PillCornerRadius`, `FocusRingThickness`, and `StandardBorderThickness`.

Typography tokens are exposed as font family resources and explicit `TextBlock` styles: `AppFontFamily`, `ResultFontFamily`, `MonospaceFontFamily`, `AppTitleTextStyle`, `PageTitleTextStyle`, `SectionTitleTextStyle`, `BodyTextStyle`, `ResultTextStyle`, `ButtonTextStyle`, `CaptionTextStyle`, `StatusTextStyle`, and `MonospaceTextStyle`.

Motion tokens are exposed as `MotionFastDuration`, `MotionNormalDuration`, `MotionSlowDuration`, `MotionEaseOut`, and `MotionEaseInOut`.

Reusable control styles are explicit resources: `KeyboardFocusVisualStyle`, `BaseButtonStyle`, `PrimaryButtonStyle`, `SecondaryButtonStyle`, `GhostButtonStyle`, `DangerButtonStyle`, `IconButtonStyle`, `CardBorderStyle`, `ResultCardStyle`, `StatusPillStyle`, `CardGroupBoxStyle`, `ReadOnlyTextBoxStyle`, `ResultTextBoxStyle`, and `AppToolTipStyle`.

`MainWindow.xaml` and `RecordingOverlay.xaml` consume these resources directly while preserving existing control names, event handlers, enabled states, tooltips, wrapping, scroll settings, and overlay window behavior.
