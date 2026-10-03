using FeatherScribe.App;
using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public sealed class UserFacingTextTests
{
    [Theory]
    [InlineData(FormattingMode.NoFormat, "未整形")]
    [InlineData(FormattingMode.PlainFast, "整形・軽量")]
    [InlineData(FormattingMode.PlainQuality, "整形・高品質")]
    [InlineData(FormattingMode.Polite, "丁寧文")]
    [InlineData(FormattingMode.Bullet, "箇条書き")]
    [InlineData(FormattingMode.Memo, "メモ")]
    [InlineData(FormattingMode.DevInstruction, "開発指示")]
    public void ModeLabel_UsesGlossaryLabel(FormattingMode mode, string expected)
    {
        Assert.Equal(expected, UserFacingText.ModeLabel(mode));
    }

    [Fact]
    public void ModeLabel_CoversEveryMode()
    {
        foreach (var mode in Enum.GetValues<FormattingMode>())
        {
            Assert.NotEqual(mode.ToString(), UserFacingText.ModeLabel(mode));
        }
    }

    [Theory]
    [InlineData(PipelineStage.Transcribing, "文字起こし中…")]
    [InlineData(PipelineStage.Formatting, "整形中…")]
    [InlineData(PipelineStage.Outputting, "貼り付け中…")]
    public void ForStage_ActiveStages(PipelineStage stage, string expected)
    {
        Assert.Equal(expected, UserFacingText.ForStage(stage, FormattingMode.PlainFast, null));
    }

    [Fact]
    public void ForStage_RecordingShowsModeLabel()
    {
        Assert.Equal("録音中…（整形・軽量）", UserFacingText.ForStage(PipelineStage.Recording, FormattingMode.PlainFast, null));
        Assert.Equal("録音中…（未整形）", UserFacingText.ForStage(PipelineStage.Recording, FormattingMode.NoFormat, null));
    }

    [Fact]
    public void ForStage_CompletedDoesNotAppendCoreNote()
    {
        Assert.Equal(
            "完了",
            UserFacingText.ForStage(
                PipelineStage.Completed,
                null,
                "LLM整形が無効設定 (llm.enabled=false) のため未整形で出力しました"));
        Assert.Equal("完了", UserFacingText.ForStage(PipelineStage.Completed, null, null));
    }

    [Fact]
    public void ForStage_FailedShowsShortReason()
    {
        Assert.Equal("失敗しました: 文字起こしに失敗しました", UserFacingText.ForStage(PipelineStage.Failed, null, "文字起こしに失敗しました\ndetail"));
        Assert.Equal("失敗しました: 不明なエラー", UserFacingText.ForStage(PipelineStage.Failed, null, null));
    }

    [Fact]
    public void ForResult_MapsEveryCase()
    {
        Assert.Equal(
            "未整形の文章を貼り付けました・整形中…",
            UserFacingText.ForResult(new PipelineResult(true, "raw", BackgroundFormattingStarted: true, false, true, null)));
        Assert.Equal(
            "整形できなかったため、未整形の文章を貼り付けました",
            UserFacingText.ForResult(new PipelineResult(true, "raw", false, UsedFallback: true, true, "timeout")));
        Assert.Equal(
            "貼り付けできませんでした・結果は画面に保持しています",
            UserFacingText.ForResult(new PipelineResult(true, "text", false, false, OutputSucceeded: false, "paste target unavailable")));
        Assert.Equal(
            "完了",
            UserFacingText.ForResult(new PipelineResult(true, "text", false, false, true, "LLM整形が無効設定 (llm.enabled=false) のため未整形で出力しました")));
        Assert.Equal(
            "失敗しました: 文字起こしに失敗しました: empty_transcript",
            UserFacingText.ForResult(new PipelineResult(false, null, false, false, false, "文字起こしに失敗しました: empty_transcript")));
    }

    [Fact]
    public void ForResult_OutputFailureWinsOverRawPastedState()
    {
        // The status never claims the text was pasted when the paste did not happen.
        Assert.Equal(
            UserFacingText.StatusOutputFailed,
            UserFacingText.ForResult(new PipelineResult(true, "raw", BackgroundFormattingStarted: true, false, OutputSucceeded: false, "paste target unavailable")));
    }

    [Fact]
    public void ForResult_PasteTargetUnavailable_SaysTextIsOnClipboard()
    {
        // The paste guard copied the text to the clipboard before failing (ClipboardOnly).
        Assert.Equal(
            "貼り付け先が見つからないため、クリップボードにコピーしました",
            UserFacingText.ForResult(PasteTargetUnavailable(backgroundStarted: false)));
        Assert.Equal(
            "貼り付け先が見つからないため、クリップボードにコピーしました",
            UserFacingText.ForResult(PasteTargetUnavailable(backgroundStarted: true)));
    }

    [Fact]
    public void ForResult_OtherOutputFailure_KeepsGenericTextWithoutClipboardClaim()
    {
        // e.g. a failed Ctrl+V with restoreClipboard=true: the user's clipboard was restored.
        var result = new PipelineResult(
            true, "text", false, false, OutputSucceeded: false, "SendInput failed", OutputErrorMessage: "SendInput failed");

        Assert.Equal("貼り付けできませんでした・結果は画面に保持しています", UserFacingText.ForResult(result));
        Assert.Equal(
            new TrayNotice("貼り付けできませんでした", "結果は画面に保持しています。画面からクリップボードにコピーできます。"),
            UserFacingText.CompletionNotice(result));
    }

    [Fact]
    public void PasteTargetUnavailable_OnlyMatchesFailedOutputWithGuardError()
    {
        Assert.True(PasteTargetUnavailableException.IsCauseOf(PasteTargetUnavailable(backgroundStarted: false)));
        // ErrorMessage alone is not trusted (it can carry other notes); OutputErrorMessage decides.
        Assert.False(PasteTargetUnavailableException.IsCauseOf(
            new PipelineResult(true, "text", false, false, false, "paste target unavailable")));
        Assert.False(PasteTargetUnavailableException.IsCauseOf(
            new PipelineResult(true, "text", false, false, true, null, OutputErrorMessage: "paste target unavailable")));
        Assert.Equal("paste target unavailable", new PasteTargetUnavailableException().Message);
        Assert.IsAssignableFrom<InvalidOperationException>(new PasteTargetUnavailableException());
    }

    [Fact]
    public void ForBackgroundFormatting_MapsEveryCase()
    {
        Assert.Equal(
            "整形完了・コピーまたは貼り付けできます",
            UserFacingText.ForBackgroundFormatting(Background("formatted", null), rawPasted: true));
        Assert.Equal(
            "整形候補があります（文末表現が変化）・確認して採用できます",
            UserFacingText.ForBackgroundFormatting(Background(null, "整形結果を破棄しました: request_phrase_removed", "candidate"), rawPasted: true));
        Assert.Equal(
            "整形できませんでした・未整形の文章は貼り付け済みです",
            UserFacingText.ForBackgroundFormatting(Background(null, "timeout"), rawPasted: true));
    }

    [Fact]
    public void ForBackgroundFormatting_RawNotPasted_NeverClaimsPaste()
    {
        Assert.Equal(
            "整形完了・コピーまたは貼り付けできます",
            UserFacingText.ForBackgroundFormatting(Background("formatted", null), rawPasted: false));
        Assert.Equal(
            "整形候補があります（文末表現が変化）・確認して採用できます",
            UserFacingText.ForBackgroundFormatting(Background(null, "整形結果を破棄しました: request_phrase_removed", "candidate"), rawPasted: false));
        Assert.Equal(
            "整形できませんでした・未整形の文章は画面に保持しています",
            UserFacingText.ForBackgroundFormatting(Background(null, "LLM へ接続できません"), rawPasted: false));
    }

    [Fact]
    public void BackgroundTrayBodies_DependOnRawPasted()
    {
        var rejected = Background(null, "整形結果を破棄しました: heading_or_label", "candidate");

        Assert.Equal(
            "理由: 見出し・ラベルが混入\n未整形の文章は貼り付け済みです。整形候補は画面で確認して採用できます。",
            UserFacingText.NotifyBackgroundRejectedBody(rejected, rawPasted: true));
        Assert.Equal(
            "理由: 見出し・ラベルが混入\n未整形の文章は画面に保持しています。整形候補は画面で確認して採用できます。",
            UserFacingText.NotifyBackgroundRejectedBody(rejected, rawPasted: false));
        Assert.Equal("未整形の文章は貼り付け済みです。", UserFacingText.NotifyBackgroundFailedBodyFor(rawPasted: true));
        Assert.Equal("未整形の文章は画面に保持しています。", UserFacingText.NotifyBackgroundFailedBodyFor(rawPasted: false));
    }

    [Fact]
    public void ForReformatStarted_DistinguishesQuality()
    {
        Assert.Equal("再整形中…（高品質）", UserFacingText.ForReformatStarted(FormattingMode.PlainQuality));
        Assert.Equal("再整形中…", UserFacingText.ForReformatStarted(FormattingMode.PlainFast));
        Assert.Equal("再整形中…", UserFacingText.ForReformatStarted(FormattingMode.Polite));
    }

    [Fact]
    public void ShortReason_UsesFirstTrimmedLine()
    {
        Assert.Equal("first line", UserFacingText.ShortReason("  first line  \r\nsecond line"));
        Assert.Equal("only", UserFacingText.ShortReason("\n  only\nnext"));
    }

    [Fact]
    public void ShortReason_TruncatesTo80CharactersWithEllipsis()
    {
        var exact = new string('あ', 80);
        Assert.Equal(exact, UserFacingText.ShortReason(exact));

        var longer = new string('あ', 81);
        Assert.Equal(new string('あ', 80) + "…", UserFacingText.ShortReason(longer));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void ShortReason_EmptyIsUnknownError(string? message)
    {
        Assert.Equal("不明なエラー", UserFacingText.ShortReason(message));
    }

    [Theory]
    [InlineData("markdown_structure", "見出し・箇条書きなどの形式が混入")]
    [InlineData("heading_or_label", "見出し・ラベルが混入")]
    [InlineData("request_phrase_removed", "文末表現が変化")]
    [InlineData("time_range_changed", "時刻範囲表現が変化")]
    [InlineData("added_forbidden_verb", "原文にない動詞が追加")]
    [InlineData("uncertain_time_guessed", "不確実な時刻を断定補正")]
    [InlineData("something_new", "something_new")]
    public void ToDisplayReason_PreservesMapping(string reason, string expected)
    {
        Assert.Equal(expected, UserFacingText.ToDisplayReason(reason));
    }

    [Fact]
    public void TrayMenuLabels_AreExact()
    {
        Assert.Equal("画面を表示", UserFacingText.TrayShowWindow);
        Assert.Equal("再整形", UserFacingText.TrayReformat);
        Assert.Equal("直近の結果をクリップボードにコピー", UserFacingText.TrayCopyLatest);
        Assert.Equal("直近の結果を直前の入力先へ貼り付け", UserFacingText.TrayPasteLatest);
        Assert.Equal("終了", UserFacingText.TrayExit);
        Assert.Equal("FeatherScribe - ローカル音声入力", UserFacingText.TrayToolTip);
    }

    [Fact]
    public void TrayNotifications_AreExact()
    {
        Assert.Equal("FeatherScribe エラー", UserFacingText.NotifyFailureTitle);
        Assert.Equal("失敗しました: boom", UserFacingText.NotifyFailureBody("boom\nstack"));
        Assert.Equal("整形できませんでした", UserFacingText.NotifyFallbackTitle);
        Assert.Equal("未整形の文章を貼り付けました。", UserFacingText.NotifyFallbackBody);
        Assert.Equal("貼り付けできませんでした", UserFacingText.NotifyOutputFailedTitle);
        Assert.Equal("結果は画面に保持しています。画面からクリップボードにコピーできます。", UserFacingText.NotifyOutputFailedBody);
        Assert.Equal("整形完了", UserFacingText.NotifyBackgroundFormattedTitle);
        Assert.Equal(
            "整形結果は「クリップボードにコピー」または「直前の入力先へ貼り付け」で使えます（自動では置き換えません）。",
            UserFacingText.NotifyBackgroundFormattedBody);
        Assert.Equal("整形候補があります", UserFacingText.NotifyBackgroundRejectedTitle);
        Assert.Equal(
            "理由: 見出し・ラベルが混入\n未整形の文章は貼り付け済みです。整形候補は画面で確認して採用できます。",
            UserFacingText.NotifyBackgroundRejectedBody(Background(null, "整形結果を破棄しました: heading_or_label", "candidate"), rawPasted: true));
        Assert.Equal("整形できませんでした", UserFacingText.NotifyBackgroundFailedTitle);
        Assert.Equal("未整形の文章は貼り付け済みです。", UserFacingText.NotifyBackgroundFailedBody);
        Assert.Equal("未整形の文章は画面に保持しています。", UserFacingText.NotifyBackgroundFailedBodyRawNotPasted);
        Assert.Equal("貼り付け先が見つからないため、クリップボードにコピーしました。", UserFacingText.NotifyPasteTargetUnavailableBody);
    }

    [Theory]
    // success, backgroundStarted, usedFallback, outputSucceeded, expected title (null = no notice)
    [InlineData(false, false, false, false, "FeatherScribe エラー")]
    [InlineData(false, false, true, false, "FeatherScribe エラー")]
    [InlineData(true, false, true, false, "貼り付けできませんでした")]
    [InlineData(true, true, false, false, "貼り付けできませんでした")]
    [InlineData(true, false, false, false, "貼り付けできませんでした")]
    [InlineData(true, false, true, true, "整形できませんでした")]
    [InlineData(true, true, false, true, null)]
    [InlineData(true, false, false, true, null)]
    public void CompletionNotice_FollowsFailurePasteFailedFallbackOrder(
        bool success,
        bool backgroundStarted,
        bool usedFallback,
        bool outputSucceeded,
        string? expectedTitle)
    {
        var result = new PipelineResult(
            success,
            success ? "text" : null,
            backgroundStarted,
            usedFallback,
            outputSucceeded,
            success ? null : "boom");

        var notice = UserFacingText.CompletionNotice(result);

        Assert.Equal(expectedTitle, notice?.Title);
    }

    [Fact]
    public void CompletionNotice_UsesExactBodies()
    {
        Assert.Equal(
            new TrayNotice("FeatherScribe エラー", "失敗しました: boom"),
            UserFacingText.CompletionNotice(new PipelineResult(false, null, false, false, false, "boom\ndetail")));
        Assert.Equal(
            new TrayNotice("貼り付けできませんでした", "結果は画面に保持しています。画面からクリップボードにコピーできます。"),
            UserFacingText.CompletionNotice(new PipelineResult(true, "text", false, true, false, "timeout")));
        Assert.Equal(
            new TrayNotice("貼り付けできませんでした", "貼り付け先が見つからないため、クリップボードにコピーしました。"),
            UserFacingText.CompletionNotice(PasteTargetUnavailable(backgroundStarted: true)));
        Assert.Equal(
            new TrayNotice("整形できませんでした", "未整形の文章を貼り付けました。"),
            UserFacingText.CompletionNotice(new PipelineResult(true, "text", false, true, true, "timeout")));
    }

    [Fact]
    public void IsRejectedCandidate_OnlyForValidatorRejection()
    {
        Assert.True(UserFacingText.IsRejectedCandidate(Background(null, "整形結果を破棄しました: markdown_structure")));
        Assert.True(UserFacingText.IsRejectedCandidate(Background(null, "other", "candidate")));
        Assert.False(UserFacingText.IsRejectedCandidate(Background(null, "timeout")));
        Assert.False(UserFacingText.IsRejectedCandidate(Background("formatted", null)));
    }

    [Fact]
    public void OperationGuide_IsCompactAndHasNoConfigKeys()
    {
        var guide = UserFacingText.OperationGuide(new HotkeySettings(), llmEnabled: true, FormattingMode.Polite);

        Assert.Equal(
            "キーを押して録音、もう一度押して停止します。\n" +
            "Ctrl+Shift+F8　未整形\n" +
            "Ctrl+Shift+F9　整形・軽量\n" +
            "Ctrl+Shift+F10　整形・高品質\n" +
            "Ctrl+Shift+F11　丁寧文\n" +
            "Ctrl+Shift+F12　箇条書き\n" +
            "Ctrl+Alt+Shift+M　メモ\n" +
            "Ctrl+Alt+Shift+D　開発指示\n" +
            "Ctrl+Shift+F7　選択テキストを編集（丁寧文）\n" +
            "LLM整形: オン",
            guide);
        Assert.DoesNotContain("llm.enabled", guide);
        Assert.DoesNotContain("\t", guide);
    }

    [Fact]
    public void OperationGuide_UsesConfiguredHotkeysAndLlmOff()
    {
        var hotkeys = new HotkeySettings { NoFormat = "Ctrl+Alt+1", Memo = "Ctrl+Alt+9", EditSelection = "Ctrl+Alt+E" };
        var lines = UserFacingText.OperationGuide(hotkeys, llmEnabled: false, FormattingMode.Bullet).Split('\n');

        Assert.Equal(10, lines.Length);
        Assert.Equal("Ctrl+Alt+1　未整形", lines[1]);
        Assert.Equal("Ctrl+Alt+9　メモ", lines[6]);
        Assert.Equal("Ctrl+Alt+E　選択テキストを編集（箇条書き）", lines[8]);
        Assert.Equal("LLM整形: オフ（どのキーでも未整形で入力します）", lines[9]);
    }

    [Fact]
    public void HotkeyFailureLine_OneLinePerKey()
    {
        var line = UserFacingText.HotkeyFailureLine(
            new HotkeyRegistrationFailure(FormattingMode.PlainFast, "Ctrl+Shift+F9", "他アプリが登録済み (Win32Error=1409)"));

        Assert.Equal("⚠ Ctrl+Shift+F9（整形・軽量）は使えません: 他アプリが登録済み (Win32Error=1409)", line);
    }

    private static BackgroundFormattingResult Background(string? formatted, string? error, string? rejected = null)
        => new(Guid.NewGuid(), FormattingMode.PlainFast, formatted, error, rejected);

    private static PipelineResult PasteTargetUnavailable(bool backgroundStarted)
        => new(
            true,
            "raw",
            backgroundStarted,
            false,
            OutputSucceeded: false,
            "paste target unavailable",
            OutputErrorMessage: "paste target unavailable");
}
