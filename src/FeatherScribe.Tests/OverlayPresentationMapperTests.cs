using FeatherScribe.App;
using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public sealed class OverlayPresentationMapperTests
{
    [Fact]
    public void StageRecording_MapsToPersistentRecordingWithElapsedTimer()
    {
        var presentation = OverlayPresentationMapper.FromStage(PipelineStage.Recording);

        Assert.Equal(OverlayVisualState.Recording, presentation.State);
        Assert.True(presentation.IsPersistent);
        Assert.True(presentation.ShowsElapsed);
        Assert.Contains("録音", presentation.Text);
    }

    [Theory]
    [InlineData(PipelineStage.Transcribing, "Transcribing", "文字起こし")]
    [InlineData(PipelineStage.Formatting, "Formatting", "整え")]
    [InlineData(PipelineStage.Outputting, "Pasting", "貼り付け")]
    public void ActiveStages_MapToPersistentOverlayStates(
        PipelineStage stage,
        string expectedState,
        string expectedText)
    {
        var presentation = OverlayPresentationMapper.FromStage(stage);

        Assert.Equal(expectedState, presentation.State.ToString());
        Assert.True(presentation.IsPersistent);
        Assert.False(presentation.ShowsElapsed);
        Assert.Contains(expectedText, presentation.Text);
    }

    [Fact]
    public void StageCompleted_DoesNotChooseFinalSuccessState()
    {
        var presentation = OverlayPresentationMapper.FromStage(PipelineStage.Completed);

        Assert.Equal(OverlayVisualState.Hidden, presentation.State);
    }

    [Fact]
    public void FailedStage_MapsToTemporaryFailedStateWithoutErrorDetail()
    {
        var presentation = OverlayPresentationMapper.FromStage(PipelineStage.Failed);

        Assert.Equal(OverlayVisualState.Failed, presentation.State);
        Assert.False(presentation.IsPersistent);
        Assert.Equal(TimeSpan.FromMilliseconds(2200), presentation.AutoHideDelay);
        Assert.DoesNotContain("Exception", presentation.Text);
    }

    [Fact]
    public void RawFirstPipelineResult_KeepsFormattingOverlayVisible()
    {
        var result = new PipelineResult(
            Success: true,
            Text: "raw",
            BackgroundFormattingStarted: true,
            UsedFallback: false,
            OutputSucceeded: true,
            ErrorMessage: null);

        var presentation = OverlayPresentationMapper.FromPipelineResult(result);

        Assert.Equal(OverlayVisualState.Formatting, presentation.State);
        Assert.True(presentation.IsPersistent);
        Assert.Contains("整形中", presentation.Text);
    }

    [Theory]
    [InlineData(false, false, false, "Failed")]
    [InlineData(true, false, true, "Fallback")]
    [InlineData(true, false, false, "Warning")]
    [InlineData(true, true, false, "Completed")]
    public void PipelineResult_MapsFinalState(
        bool success,
        bool outputSucceeded,
        bool usedFallback,
        string expectedState)
    {
        var result = new PipelineResult(
            success,
            success ? "text" : null,
            BackgroundFormattingStarted: false,
            UsedFallback: usedFallback,
            OutputSucceeded: outputSucceeded,
            ErrorMessage: success ? null : "details");

        var presentation = OverlayPresentationMapper.FromPipelineResult(result);

        Assert.Equal(expectedState, presentation.State.ToString());
        Assert.False(presentation.IsPersistent);
        Assert.False(presentation.ShowsElapsed);
    }

    [Fact]
    public void BackgroundFormattingSuccess_MapsToCompleted()
    {
        var result = new BackgroundFormattingResult(
            Guid.NewGuid(),
            FormattingMode.PlainFast,
            FormattedText: "formatted",
            ErrorMessage: null);

        var presentation = OverlayPresentationMapper.FromBackgroundFormattingResult(result);

        Assert.Equal(OverlayVisualState.Completed, presentation.State);
        Assert.Equal("整形完了", presentation.Text);
    }

    [Theory]
    [InlineData("candidate", null, "Warning")]
    [InlineData(null, "整形結果を破棄しました: markdown_structure", "Warning")]
    [InlineData(null, "timeout", "Fallback")]
    public void BackgroundFormattingNonSuccess_MapsToReviewOrFallback(
        string? rejectedText,
        string? errorMessage,
        string expectedState)
    {
        var result = new BackgroundFormattingResult(
            Guid.NewGuid(),
            FormattingMode.PlainFast,
            FormattedText: null,
            ErrorMessage: errorMessage,
            RejectedText: rejectedText);

        var presentation = OverlayPresentationMapper.FromBackgroundFormattingResult(result);

        Assert.Equal(expectedState, presentation.State.ToString());
        Assert.False(presentation.IsPersistent);
        Assert.Equal(TimeSpan.FromMilliseconds(1800), presentation.AutoHideDelay);
    }
}
