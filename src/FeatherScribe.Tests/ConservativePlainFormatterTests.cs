using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public class ConservativePlainFormatterTests
{
    [Fact]
    public void Format_PreservesRequestAndTimeRange()
    {
        var result = ConservativePlainFormatter.Format(
            "今日の会議はまず午前10時からでお願いします");

        Assert.Equal("今日の会議は、まず午前10時からでお願いします。", result);
    }

    [Fact]
    public void Format_RemovesLeadingFillerWithoutChangingMeaning()
    {
        var result = ConservativePlainFormatter.Format(
            "えーと、今日の会議はまず午前10時からでお願いします");

        Assert.Equal("今日の会議は、まず午前10時からでお願いします。", result);
    }

    [Fact]
    public void Format_DoesNotGuessUncertainTime()
    {
        var result = ConservativePlainFormatter.Format(
            "今日はの会議、まずは周時からお願いします");

        Assert.Equal("今日はの会議、まずは周時からお願いします。", result);
    }
}
