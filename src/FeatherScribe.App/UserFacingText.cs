using System.Text;
using FeatherScribe.Core;

namespace FeatherScribe.App;

/// <summary>
/// Single source for the user-facing text of MainWindow status, the tray menu and tray
/// notifications, mode labels and validator display reasons. In-App Feedback messages stay
/// in <see cref="InAppFeedbackMessages"/>. Identifiers and log codes are not translated here.
/// </summary>
/// <summary>A tray balloon notification (title and body).</summary>
internal sealed record TrayNotice(string Title, string Body);

internal static class UserFacingText
{
    public const int ShortReasonMaxLength = 80;

    // --- StatusText (continuous state) ---
    public const string StatusIdle = "待機中";
    public const string StatusTranscribing = "文字起こし中…";
    public const string StatusFormatting = "整形中…";
    public const string StatusOutputting = "貼り付け中…";
    public const string StatusCompleted = "完了";
    public const string StatusRawPastedFormatting = "未整形の文章を貼り付けました・整形中…";
    public const string StatusFallback = "整形できなかったため、未整形の文章を貼り付けました";
    public const string StatusOutputFailed = "貼り付けできませんでした・結果は画面に保持しています";
    public const string StatusPasteTargetUnavailable = "貼り付け先が見つからないため、クリップボードにコピーしました";
    public const string StatusBackgroundFormatted = "整形完了・コピーまたは貼り付けできます";
    public const string StatusBackgroundFailed = "整形できませんでした・未整形の文章は貼り付け済みです";
    public const string StatusBackgroundFailedRawNotPasted = "整形できませんでした・未整形の文章は画面に保持しています";
    public const string StatusReformatting = "再整形中…";
    public const string StatusReformattingQuality = "再整形中…（高品質）";
    public const string StatusCandidateAdopted = "整形候補を採用済み";
    public const string UnknownError = "不明なエラー";

    // --- Tray menu ---
    public const string TrayShowWindow = "画面を表示";
    public const string TrayReformat = "再整形";
    public const string TrayCopyLatest = "直近の結果をクリップボードにコピー";
    public const string TrayPasteLatest = "直近の結果を直前の入力先へ貼り付け";
    public const string TrayExit = "終了";
    public const string TrayToolTip = "FeatherScribe - ローカル音声入力";

    // --- Tray notifications ---
    public const string NotifyFailureTitle = "FeatherScribe エラー";
    public const string NotifyFallbackTitle = "整形できませんでした";
    public const string NotifyFallbackBody = "未整形の文章を貼り付けました。";
    public const string NotifyOutputFailedTitle = "貼り付けできませんでした";
    public const string NotifyOutputFailedBody = "結果は画面に保持しています。画面からクリップボードにコピーできます。";
    public const string NotifyPasteTargetUnavailableBody = "貼り付け先が見つからないため、クリップボードにコピーしました。";
    public const string NotifyBackgroundFormattedTitle = "整形完了";
    public const string NotifyBackgroundFormattedBody =
        "整形結果は「クリップボードにコピー」または「直前の入力先へ貼り付け」で使えます（自動では置き換えません）。";
    public const string NotifyBackgroundRejectedTitle = "整形候補があります";
    public const string NotifyBackgroundFailedTitle = "整形できませんでした";
    public const string NotifyBackgroundFailedBody = "未整形の文章は貼り付け済みです。";
    public const string NotifyBackgroundFailedBodyRawNotPasted = "未整形の文章は画面に保持しています。";

    // --- Operation guide ---
    public const string GuideIntro = "キーを押して録音、もう一度押して停止します。";
    public const string GuideLlmOn = "LLM整形: オン";
    public const string GuideLlmOff = "LLM整形: オフ（どのキーでも未整形で入力します）";
    public const char GuideSeparator = '　';

    // --- Selected-text editing (Phase UX-1) ---
    public const string SelectionEditActionLabel = "選択テキスト編集";
    public const string SelectionEditReplaced = "選択範囲を置き換えました";
    public const string SelectionEditLlmDisabled = "LLM整形がオフのため、選択テキストの編集は使えません";
    public const string SelectionEditBusy = "処理中のため、選択テキストの編集を開始できません";
    public const string SelectionEditCaptureFailed = "選択テキストを取得できませんでした";
    public const string SelectionEditFailed = "編集できなかったため、選択テキストは変更していません";
    public const string SelectionEditTargetChanged = "入力先が変わったため置き換えませんでした。編集結果はクリップボードにあります";
    public const string SelectionEditReplaceFailed = "選択範囲を置き換えできませんでした";
    public const string NotifySelectionEditTitle = "選択テキストの編集";

    // Prefix of the formatter message when the validator discarded the formatted text (Infrastructure contract).
    private const string DiscardedPrefix = "整形結果を破棄しました:";

    public static string ModeLabel(FormattingMode mode)
        => mode switch
        {
            FormattingMode.NoFormat => "未整形",
            FormattingMode.PlainFast => "整形・軽量",
            FormattingMode.PlainQuality => "整形・高品質",
            FormattingMode.Polite => "丁寧文",
            FormattingMode.Bullet => "箇条書き",
            FormattingMode.Memo => "メモ",
            FormattingMode.DevInstruction => "開発指示",
            _ => mode.ToString(),
        };

    /// <summary>Validator reason code → display reason. Unknown codes are shown as is.</summary>
    public static string ToDisplayReason(string reason)
        => reason switch
        {
            "markdown_structure" => "見出し・箇条書きなどの形式が混入",
            "heading_or_label" => "見出し・ラベルが混入",
            "request_phrase_removed" => "文末表現が変化",
            "time_range_changed" => "時刻範囲表現が変化",
            "added_forbidden_verb" => "原文にない動詞が追加",
            "uncertain_time_guessed" => "不確実な時刻を断定補正",
            _ => reason,
        };

    /// <summary>First line of the message, trimmed, at most 80 characters then "…"; empty → "不明なエラー".</summary>
    public static string ShortReason(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return UnknownError;
        }

        var trimmed = message.Trim();
        var lineEnd = trimmed.IndexOfAny(['\r', '\n']);
        var firstLine = (lineEnd >= 0 ? trimmed[..lineEnd] : trimmed).Trim();
        if (firstLine.Length == 0)
        {
            return UnknownError;
        }

        return firstLine.Length > ShortReasonMaxLength
            ? string.Concat(firstLine.AsSpan(0, ShortReasonMaxLength), "…")
            : firstLine;
    }

    public static string Failed(string? message) => $"失敗しました: {ShortReason(message)}";

    public static string Recording(FormattingMode? mode)
        => mode is { } activeMode ? $"録音中…（{ModeLabel(activeMode)}）" : "録音中…";

    /// <summary>Status text for a pipeline stage event; null keeps the current text.</summary>
    public static string? ForStage(PipelineStage stage, FormattingMode? activeMode, string? message)
        => stage switch
        {
            PipelineStage.Recording => Recording(activeMode),
            PipelineStage.Transcribing => StatusTranscribing,
            PipelineStage.Formatting => StatusFormatting,
            PipelineStage.Outputting => StatusOutputting,
            // The Core note message (e.g. why the text was not formatted) is not appended.
            PipelineStage.Completed => StatusCompleted,
            PipelineStage.Failed => Failed(message),
            _ => null,
        };

    public static string ForResult(PipelineResult result)
    {
        if (!result.Success)
        {
            return Failed(result.ErrorMessage);
        }

        // A failed paste is reported first so the status never claims the text was pasted.
        // Only the paste guard case says the clipboard holds the result: after any other output
        // failure the clipboard may have been restored to the user's previous content.
        if (!result.OutputSucceeded)
        {
            return PasteTargetUnavailableException.IsCauseOf(result)
                ? StatusPasteTargetUnavailable
                : StatusOutputFailed;
        }

        if (result.BackgroundFormattingStarted)
        {
            return StatusRawPastedFormatting;
        }

        return result.UsedFallback ? StatusFallback : StatusCompleted;
    }

    /// <param name="rawPasted">True only when the raw output of the dictation this result belongs to is
    /// known to have been pasted; false (also when unknown) never claims a paste.</param>
    public static string ForBackgroundFormatting(BackgroundFormattingResult result, bool rawPasted)
    {
        if (result.FormattedText is not null)
        {
            return StatusBackgroundFormatted;
        }

        if (IsRejectedCandidate(result))
        {
            return $"整形候補があります（{BackgroundDisplayReason(result)}）・確認して採用できます";
        }

        return rawPasted ? StatusBackgroundFailed : StatusBackgroundFailedRawNotPasted;
    }

    public static string ForReformatStarted(FormattingMode mode)
        => mode == FormattingMode.PlainQuality ? StatusReformattingQuality : StatusReformatting;

    /// <summary>True when the validator rejected the formatted text (a candidate may be adopted).</summary>
    public static bool IsRejectedCandidate(BackgroundFormattingResult result)
        => result.FormattedText is null
            && (TryGetDiscardedReason(result.ErrorMessage, out _) || result.RejectedText is not null);

    public static string BackgroundDisplayReason(BackgroundFormattingResult result)
        => TryGetDiscardedReason(result.ErrorMessage, out var reason)
            ? (reason.Length > 0 ? ToDisplayReason(reason) : UnknownError)
            : ShortReason(result.ErrorMessage);

    public static string NotifyFailureBody(string? message) => Failed(message);

    /// <summary>
    /// Tray notice for a completed dictation, or null when none is shown. Same priority as
    /// StatusText and the overlay: failure → paste failed → fallback. A raw-first result with
    /// background formatting shows no notice here (the background result notifies later).
    /// </summary>
    public static TrayNotice? CompletionNotice(PipelineResult result)
    {
        if (!result.Success)
        {
            return new TrayNotice(NotifyFailureTitle, NotifyFailureBody(result.ErrorMessage));
        }

        if (!result.OutputSucceeded)
        {
            return new TrayNotice(
                NotifyOutputFailedTitle,
                PasteTargetUnavailableException.IsCauseOf(result) ? NotifyPasteTargetUnavailableBody : NotifyOutputFailedBody);
        }

        return result.UsedFallback
            ? new TrayNotice(NotifyFallbackTitle, NotifyFallbackBody)
            : null;
    }

    /// <param name="rawPasted">Same meaning as in <see cref="ForBackgroundFormatting"/>.</param>
    public static string NotifyBackgroundRejectedBody(BackgroundFormattingResult result, bool rawPasted)
    {
        var state = rawPasted ? "貼り付け済みです" : "画面に保持しています";
        return $"理由: {BackgroundDisplayReason(result)}\n未整形の文章は{state}。整形候補は画面で確認して採用できます。";
    }

    /// <param name="rawPasted">Same meaning as in <see cref="ForBackgroundFormatting"/>.</param>
    public static string NotifyBackgroundFailedBodyFor(bool rawPasted)
        => rawPasted ? NotifyBackgroundFailedBody : NotifyBackgroundFailedBodyRawNotPasted;

    /// <summary>
    /// Compact operation guide: one hotkey per line, the selected-text editing hotkey (omitted when it is
    /// disabled), then the LLM formatting state.
    /// </summary>
    public static string OperationGuide(HotkeySettings hotkeys, bool llmEnabled, FormattingMode selectionEditMode)
    {
        var builder = new StringBuilder(GuideIntro);
        foreach (var (mode, hotkey) in GuideHotkeys(hotkeys))
        {
            builder.Append('\n').Append(hotkey).Append(GuideSeparator).Append(ModeLabel(mode));
        }

        if (!string.IsNullOrWhiteSpace(hotkeys.EditSelection))
        {
            builder.Append('\n').Append(SelectionEditGuideLine(hotkeys.EditSelection, selectionEditMode));
        }

        builder.Append('\n').Append(llmEnabled ? GuideLlmOn : GuideLlmOff);
        return builder.ToString();
    }

    /// <summary>Operation guide line of the selected-text editing hotkey.</summary>
    public static string SelectionEditGuideLine(string hotkey, FormattingMode mode)
        => $"{hotkey}{GuideSeparator}選択テキストを編集（{ModeLabel(mode)}）";

    /// <summary>One line per hotkey that could not be registered.</summary>
    public static string HotkeyFailureLine(HotkeyRegistrationFailure failure)
        => HotkeyFailureLine(failure.HotkeyText, ModeLabel(failure.Mode), failure.Reason);

    /// <summary>Failure line of an extra action hotkey (e.g. selected-text editing), same wording as the modes.</summary>
    public static string HotkeyFailureLine(HotkeyActionRegistration registration)
        => HotkeyFailureLine(registration.HotkeyText, registration.Name, registration.FailureReason ?? UnknownError);

    /// <summary>The single notice of each selection edit result.</summary>
    public static string SelectionEditNotice(SelectionEditStatus status)
        => status switch
        {
            SelectionEditStatus.Replaced => SelectionEditReplaced,
            SelectionEditStatus.LlmDisabled => SelectionEditLlmDisabled,
            SelectionEditStatus.Busy => SelectionEditBusy,
            SelectionEditStatus.CaptureFailed => SelectionEditCaptureFailed,
            SelectionEditStatus.EditFailed => SelectionEditFailed,
            SelectionEditStatus.TargetChanged => SelectionEditTargetChanged,
            SelectionEditStatus.ReplaceFailed => SelectionEditReplaceFailed,
            _ => SelectionEditFailed,
        };

    /// <summary>
    /// Tray notice for a selection edit, or null (overlay only). Shown when the user has something to act on
    /// or waited for nothing: the edited text is on the clipboard, the edit failed, or the replacement failed.
    /// </summary>
    public static TrayNotice? SelectionEditTrayNotice(SelectionEditOutcome outcome)
        => outcome.Status is SelectionEditStatus.TargetChanged
            or SelectionEditStatus.EditFailed
            or SelectionEditStatus.ReplaceFailed
            ? new TrayNotice(NotifySelectionEditTitle, outcome.Notice)
            : null;

    private static string HotkeyFailureLine(string hotkeyText, string label, string reason)
        => $"⚠ {hotkeyText}（{label}）は使えません: {reason}";

    private static IEnumerable<(FormattingMode Mode, string Hotkey)> GuideHotkeys(HotkeySettings hotkeys)
    {
        yield return (FormattingMode.NoFormat, hotkeys.NoFormat);
        yield return (FormattingMode.PlainFast, hotkeys.PlainFast);
        yield return (FormattingMode.PlainQuality, hotkeys.PlainQuality);
        yield return (FormattingMode.Polite, hotkeys.Polite);
        yield return (FormattingMode.Bullet, hotkeys.Bullet);
        yield return (FormattingMode.Memo, hotkeys.Memo);
        yield return (FormattingMode.DevInstruction, hotkeys.DevInstruction);
    }

    private static bool TryGetDiscardedReason(string? errorMessage, out string reason)
    {
        if (errorMessage?.StartsWith(DiscardedPrefix, StringComparison.Ordinal) == true)
        {
            reason = errorMessage[DiscardedPrefix.Length..].Trim();
            return true;
        }

        reason = "";
        return false;
    }
}
