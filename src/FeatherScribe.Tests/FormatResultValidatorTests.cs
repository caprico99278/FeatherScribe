using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public class FormatResultValidatorTests
{
    private const string Raw = "えーと、今日はいい天気なので、散歩に行こうと思います。";

    [Fact]
    public void Validate_NormalOutput_IsValid()
    {
        var result = FormatResultValidator.Validate(Raw, "今日はいい天気なので、散歩に行こうと思います。");

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Validate_EmptyOutput_IsInvalid(string? formatted)
    {
        var result = FormatResultValidator.Validate(Raw, formatted);

        Assert.False(result.IsValid);
        Assert.Equal("empty_output", result.Reason);
    }

    [Theory]
    [InlineData("以下が整形結果です。\n今日はいい天気です。")]
    [InlineData("こちらが整形したテキストです: 今日はいい天気です。")]
    [InlineData("承知しました。今日はいい天気です。")]
    [InlineData("申し訳ありませんが、整形できません。")]
    [InlineData("Here is the formatted text: ...")]
    [InlineData("I cannot help with that.")]
    public void Validate_PreambleOrRefusal_IsInvalid(string formatted)
    {
        var result = FormatResultValidator.Validate(Raw, formatted);

        Assert.False(result.IsValid);
        Assert.Equal("preamble_or_refusal", result.Reason);
    }

    [Fact]
    public void Validate_ExcessivelyLongOutput_IsInvalid()
    {
        var formatted = string.Concat(Enumerable.Repeat("今日はいい天気です。", 100));

        var result = FormatResultValidator.Validate(Raw, formatted);

        Assert.False(result.IsValid);
        Assert.Equal("output_too_long", result.Reason);
    }

    [Fact]
    public void Validate_SlightlyLongerOutput_IsValid()
    {
        // 句読点の補完などで多少長くなるのは許容する
        var result = FormatResultValidator.Validate(
            Raw,
            Raw + "また、明日も晴れるようです。");

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("## 会議予定\n\n今日の会議は、まず午前10時からでお願いします。")]
    [InlineData("* 午前10時より、本日の会議を開始する。")]
    [InlineData("- 午前10時より、本日の会議を開始する。")]
    [InlineData("今日の会議です。\n* 午前10時より開始")]
    public void Validate_MarkdownStructure_IsInvalid(string formatted)
    {
        var result = FormatResultValidator.Validate(
            "今日の会議は、まず、午前10時からでお願いします。",
            formatted);

        Assert.False(result.IsValid);
        Assert.Equal("markdown_structure", result.Reason);
    }

    [Fact]
    public void Validate_RemovesRequestPhrase_IsInvalid()
    {
        var result = FormatResultValidator.Validate(
            "今日の会議は、まず、午前10時からでお願いします。",
            "今日の会議は、まず午前10時からです。");

        Assert.False(result.IsValid);
        Assert.Equal("request_phrase_removed", result.Reason);
    }

    [Fact]
    public void Validate_ChangesTimeRangeToStart_IsInvalid()
    {
        var result = FormatResultValidator.Validate(
            "今日の会議は、まず、午前10時からでお願いします。",
            "今日の会議は、まず午前10時に開始でお願いします。");

        Assert.False(result.IsValid);
        Assert.Equal("time_range_changed", result.Reason);
    }

    [Theory]
    [InlineData("今日の会議は、午前10時に開始します。")]
    [InlineData("今日の会議は、まず午前10時に始めます。")]
    [InlineData("今日の会議は、午前10時を予定しています。")]
    public void Validate_AddsForbiddenVerb_IsInvalid(string formatted)
    {
        var result = FormatResultValidator.Validate(
            "今日の会議は午前10時です。",
            formatted);

        Assert.False(result.IsValid);
        Assert.Equal("added_forbidden_verb", result.Reason);
    }

    [Fact]
    public void Validate_GuessesUncertainTime_IsInvalid()
    {
        var result = FormatResultValidator.Validate(
            "今日はの会議、まずは周時からお願いします。",
            "今日の会議、まずは午前10時からお願いします。");

        Assert.False(result.IsValid);
        Assert.Equal("uncertain_time_guessed", result.Reason);
    }

    [Theory]
    [InlineData("修正後: 今日の会議は、まず午前10時からでお願いします。")]
    [InlineData("文字起こし結果: 今日の会議は、まず午前10時からでお願いします。")]
    [InlineData("会議予定\n今日の会議は、まず午前10時からでお願いします。")]
    public void Validate_ForbiddenLabel_IsInvalid(string formatted)
    {
        var result = FormatResultValidator.Validate(
            "今日の会議は、まず、午前10時からでお願いします。",
            formatted);

        Assert.False(result.IsValid);
        Assert.Equal("heading_or_label", result.Reason);
    }

    [Fact]
    public void Validate_MinimalRequestTimeEdit_IsValid()
    {
        var result = FormatResultValidator.Validate(
            "今日の会議は、まず、午前10時からでお願いします。",
            "今日の会議は、まず午前10時からでお願いします。");

        Assert.True(result.IsValid);
    }
}
