using FeatherScribe.App;

namespace FeatherScribe.Tests;

public sealed class InAppFeedbackStateTests
{
    private static readonly InAppFeedback Copied =
        new(InAppFeedbackKind.Success, InAppFeedbackMessages.CopySucceeded);

    private static readonly InAppFeedback CopyFailed =
        new(InAppFeedbackKind.Error, InAppFeedbackMessages.CopyFailed);

    [Fact]
    public void NewState_IsHiddenWithoutFeedback()
    {
        var state = new InAppFeedbackState();

        Assert.Equal(InAppFeedbackPhase.Hidden, state.Phase);
        Assert.Null(state.Current);
    }

    [Fact]
    public void Show_FromHidden_Enters()
    {
        var state = new InAppFeedbackState();

        var transition = state.Show(Copied);

        Assert.Equal(FeedbackTransition.Enter, transition);
        Assert.Equal(InAppFeedbackPhase.Visible, state.Phase);
        Assert.Same(Copied, state.Current);
    }

    [Fact]
    public void Show_WhileVisible_UpdatesInPlaceAndLatestWins()
    {
        var state = new InAppFeedbackState();
        state.Show(Copied);
        var versionBefore = state.Version;

        var transition = state.Show(CopyFailed);

        Assert.Equal(FeedbackTransition.UpdateInPlace, transition);
        Assert.Equal(InAppFeedbackPhase.Visible, state.Phase);
        Assert.Same(CopyFailed, state.Current);
        Assert.True(state.Version > versionBefore);
    }

    [Fact]
    public void Show_WhileHiding_RecoversFromHiding()
    {
        var state = new InAppFeedbackState();
        state.Show(Copied);
        Assert.True(state.BeginHide());

        var transition = state.Show(CopyFailed);

        Assert.Equal(FeedbackTransition.RecoverFromHiding, transition);
        Assert.Equal(InAppFeedbackPhase.Visible, state.Phase);
        Assert.Same(CopyFailed, state.Current);
    }

    [Fact]
    public void CompleteHide_WithCurrentVersion_Hides()
    {
        var state = new InAppFeedbackState();
        state.Show(Copied);
        Assert.True(state.BeginHide());
        var hideVersion = state.Version;

        Assert.True(state.CompleteHide(hideVersion));
        Assert.Equal(InAppFeedbackPhase.Hidden, state.Phase);
        Assert.Equal(FeedbackTransition.Enter, state.Show(CopyFailed));
    }

    [Fact]
    public void CompleteHide_StaleVersion_DoesNotHideNewerFeedback()
    {
        var state = new InAppFeedbackState();
        state.Show(Copied);
        Assert.True(state.BeginHide());
        var staleHideVersion = state.Version;

        // A newer feedback arrives while the first one is fading out.
        state.Show(CopyFailed);

        Assert.False(state.CompleteHide(staleHideVersion));
        Assert.Equal(InAppFeedbackPhase.Visible, state.Phase);
        Assert.Same(CopyFailed, state.Current);
    }

    [Fact]
    public void CompleteHide_StaleVersionAfterSecondHide_DoesNotCompleteTheNewerHide()
    {
        var state = new InAppFeedbackState();
        state.Show(Copied);
        state.BeginHide();
        var staleHideVersion = state.Version;
        state.Show(CopyFailed);
        state.BeginHide();

        Assert.False(state.CompleteHide(staleHideVersion));
        Assert.Equal(InAppFeedbackPhase.Hiding, state.Phase);
        Assert.True(state.CompleteHide(state.Version));
        Assert.Equal(InAppFeedbackPhase.Hidden, state.Phase);
    }

    [Fact]
    public void BeginHide_OnlyStartsFromVisible()
    {
        var state = new InAppFeedbackState();

        Assert.False(state.BeginHide());
        Assert.Equal(InAppFeedbackPhase.Hidden, state.Phase);

        state.Show(Copied);
        Assert.True(state.BeginHide());
        Assert.Equal(InAppFeedbackPhase.Hiding, state.Phase);

        var versionWhileHiding = state.Version;
        Assert.False(state.BeginHide());
        Assert.Equal(versionWhileHiding, state.Version);
        Assert.Equal(InAppFeedbackPhase.Hiding, state.Phase);
    }

    [Fact]
    public void CompleteHide_WhenNotHiding_ReturnsFalse()
    {
        var state = new InAppFeedbackState();
        Assert.False(state.CompleteHide(state.Version));

        state.Show(Copied);
        Assert.False(state.CompleteHide(state.Version));
        Assert.Equal(InAppFeedbackPhase.Visible, state.Phase);
    }

    [Theory]
    [InlineData("Success", 1400)]
    [InlineData("Info", 1400)]
    [InlineData("Warning", 2000)]
    [InlineData("Error", 2200)]
    public void DurationFor_MatchesKind(string kindName, int expectedMilliseconds)
    {
        var kind = Enum.Parse<InAppFeedbackKind>(kindName);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), InAppFeedback.DurationFor(kind));
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), new InAppFeedback(kind, "x").Duration);
    }

    [Theory]
    [InlineData("Success", "✓", "SuccessBrush")]
    [InlineData("Info", "i", "AccentBrush")]
    [InlineData("Warning", "!", "WarningBrush")]
    [InlineData("Error", "×", "DangerBrush")]
    public void GlyphAndBrush_MatchKind(string kindName, string expectedGlyph, string expectedBrushKey)
    {
        var kind = Enum.Parse<InAppFeedbackKind>(kindName);

        Assert.Equal(expectedGlyph, InAppFeedback.GlyphFor(kind));
        Assert.Equal(expectedBrushKey, InAppFeedback.GlyphBrushKeyFor(kind));
    }

    [Fact]
    public void Messages_MatchCatalogExactly()
    {
        Assert.Equal("クリップボードにコピーしました", InAppFeedbackMessages.CopySucceeded);
        Assert.Equal("直前の入力先へ貼り付けました", InAppFeedbackMessages.RepasteSucceeded);
        Assert.Equal("貼り付け先が見つからないため、クリップボードにコピーしました", InAppFeedbackMessages.RepasteTargetNotFound);
        Assert.Equal("再整形を開始しました", InAppFeedbackMessages.ReformatStarted);
        Assert.Equal("再整形を開始できませんでした", InAppFeedbackMessages.ReformatNotStarted);
        Assert.Equal("整形候補を採用しました", InAppFeedbackMessages.CandidateAdopted);
        Assert.Equal("採用できる整形候補がありません", InAppFeedbackMessages.NoCandidateToAdopt);
        Assert.Equal("コピーできませんでした", InAppFeedbackMessages.CopyFailed);
        Assert.Equal("貼り付けできませんでした", InAppFeedbackMessages.RepasteFailed);
        Assert.Equal("再整形を開始できませんでした", InAppFeedbackMessages.ReformatFailed);
        Assert.Equal("整形候補を採用できませんでした", InAppFeedbackMessages.AdoptFailed);
    }
}
