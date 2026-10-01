using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

public sealed class ClipboardRestorePolicyTests
{
    [Theory]
    [InlineData(false, OutputMode.ClipboardAndPaste, false)]
    [InlineData(false, OutputMode.ClipboardOnly, false)]
    [InlineData(true, OutputMode.ClipboardOnly, false)]
    [InlineData(true, OutputMode.ClipboardAndPaste, true)]
    public void ShouldCapture_OnlyWhenEnabledAndPasting(bool restoreEnabled, OutputMode mode, bool expected)
    {
        Assert.Equal(expected, ClipboardRestorePolicy.ShouldCapture(restoreEnabled, mode));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Decide_SequenceUnchanged_RestoresNow(bool pasteSent, bool emptySnapshot)
    {
        var kind = emptySnapshot ? SnapshotKind.Empty : SnapshotKind.Captured;
        Assert.Equal(RestoreDecision.RestoreNow, ClipboardRestorePolicy.Decide(pasteSent, 42, 42, kind));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Decide_SequenceChanged_NeverOverwritesNewerContent(bool pasteSent, bool emptySnapshot)
    {
        var kind = emptySnapshot ? SnapshotKind.Empty : SnapshotKind.Captured;
        Assert.Equal(RestoreDecision.SkipChanged, ClipboardRestorePolicy.Decide(pasteSent, 42, 43, kind));
    }

    [Theory]
    [InlineData(true, 42u, 42u)]
    [InlineData(true, 42u, 43u)]
    [InlineData(false, 42u, 42u)]
    public void Decide_UnsupportedSnapshot_Skips(bool pasteSent, uint expected, uint current)
    {
        Assert.Equal(
            RestoreDecision.SkipUnsupported,
            ClipboardRestorePolicy.Decide(pasteSent, expected, current, SnapshotKind.Unsupported));
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(0u, 5u)]
    [InlineData(5u, 0u)]
    public void Decide_SequenceNotAccessible_SkipsBecauseNoGuardIsPossible(uint expected, uint current)
    {
        Assert.Equal(
            RestoreDecision.SkipUnsupported,
            ClipboardRestorePolicy.Decide(true, expected, current, SnapshotKind.Captured));
    }

    [Theory]
    [InlineData(0L, 0, 0, true)]
    [InlineData(1_000_000L, 4096, 4096, true)]
    [InlineData(1_000_001L, 0, 0, false)]
    [InlineData(0L, 4097, 10, false)]
    [InlineData(0L, 10, 4097, false)]
    public void IsWithinSizeLimit_GuardsTextAndBitmapSize(long textChars, int width, int height, bool expected)
    {
        Assert.Equal(expected, ClipboardRestorePolicy.IsWithinSizeLimit(textChars, width, height));
    }

    [Fact]
    public void RestoreDelay_Is600Milliseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(600), ClipboardTextOutput.RestoreDelay);
    }
}
