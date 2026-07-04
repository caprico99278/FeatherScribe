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
}
