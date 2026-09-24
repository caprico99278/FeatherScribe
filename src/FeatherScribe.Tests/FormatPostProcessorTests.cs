using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public class FormatPostProcessorTests
{
    [Fact]
    public void RemoveThinkTags_RemovesReasoningBlock()
    {
        var result = FormatPostProcessor.RemoveThinkTags("<think>reasoning</think>\n整形済みです。");

        Assert.True(result.ThinkTagRemoved);
        Assert.Equal("整形済みです。", result.Text);
    }

    [Fact]
    public void RemoveThinkTags_LeavesNormalText()
    {
        var result = FormatPostProcessor.RemoveThinkTags("整形済みです。");

        Assert.False(result.ThinkTagRemoved);
        Assert.Equal("整形済みです。", result.Text);
    }
}
