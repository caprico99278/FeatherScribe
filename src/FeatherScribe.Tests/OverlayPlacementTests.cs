using FeatherScribe.App;

namespace FeatherScribe.Tests;

/// <summary>Phase UI-8: pure overlay placement math (visual_direction.md §25).</summary>
public sealed class OverlayPlacementTests
{
    [Fact]
    public void BottomCenter_CentersHorizontallyAboveTheWorkAreaBottom()
    {
        var workArea = new RectPx(0, 0, 1920, 1040);

        Assert.Equal((810, 966), OverlayPlacement.BottomCenter(workArea, 300, 50, 24));
    }

    [Fact]
    public void BottomCenter_UsesTheGivenMonitorEvenWithNegativeOrOffsetCoordinates()
    {
        // Secondary monitor left of the primary.
        Assert.Equal((-1110, 966), OverlayPlacement.BottomCenter(new RectPx(-1920, 0, 0, 1040), 300, 50, 24));

        // Secondary monitor right of the primary with the taskbar at its top.
        Assert.Equal((2730, 1006), OverlayPlacement.BottomCenter(new RectPx(1920, 40, 3840, 1080), 300, 50, 24));

        // Monitor above the primary (negative Y).
        Assert.Equal((810, -134), OverlayPlacement.BottomCenter(new RectPx(0, -1080, 1920, -40), 300, 70, 24));
    }

    [Fact]
    public void BottomCenter_AlignsAnOverlayWiderThanTheWorkAreaToTheLeftEdge()
    {
        var workArea = new RectPx(100, 0, 500, 800);

        var (x, _) = OverlayPlacement.BottomCenter(workArea, 640, 50, 24);

        Assert.Equal(100, x);
    }

    [Fact]
    public void BottomCenter_ClampsVerticallyIntoTheWorkArea()
    {
        // The margin would push the overlay above the top edge: it stays inside instead.
        Assert.Equal(10, OverlayPlacement.BottomCenter(new RectPx(0, 10, 800, 70), 200, 50, 24).Y);

        // Taller than the work area: aligned to the top edge.
        Assert.Equal(10, OverlayPlacement.BottomCenter(new RectPx(0, 10, 800, 40), 200, 50, 24).Y);
    }

    [Fact]
    public void BottomCenter_KeepsTheWholeOverlayInsideTheWorkArea()
    {
        var workAreas = new[]
        {
            new RectPx(0, 0, 1920, 1040),
            new RectPx(-2560, -200, 0, 1240),
            new RectPx(1920, 48, 3200, 1024),
            new RectPx(0, 0, 801, 601),
        };

        foreach (var workArea in workAreas)
        {
            // Oversized overlays are covered by the left-edge test above.
            foreach (var width in new[] { 1, 180, 263, 420, 945 }.Where(width => width <= workArea.Width))
            {
                foreach (var height in new[] { 1, 38, 57, 95 })
                {
                    foreach (var margin in new[] { 0, 24, 30, 36, 54 })
                    {
                        var (x, y) = OverlayPlacement.BottomCenter(workArea, width, height, margin);

                        Assert.InRange(x, workArea.Left, workArea.Right - width);
                        Assert.InRange(y, workArea.Top, workArea.Bottom - height);
                        Assert.True(workArea.Bottom - (y + height) >= Math.Min(margin, workArea.Height - height));

                        // Centered to within one pixel (odd remainders round toward the left).
                        var leftGap = x - workArea.Left;
                        var rightGap = workArea.Right - (x + width);
                        Assert.InRange(rightGap - leftGap, 0, 1);
                    }
                }
            }
        }
    }

    [Fact]
    public void BottomCenter_WithShadowInset_PlacesTheVisiblePillAndOffsetsTheWindow()
    {
        // 100% DPI: a 220x40 pill inside a window with 21 px of transparent room on every side.
        var workArea = new RectPx(0, 0, 1920, 1040);
        const int inset = 21;
        var (x, y) = OverlayPlacement.BottomCenter(workArea, 220 + (2 * inset), 40 + (2 * inset), 24, inset);

        // Pill: centered, bottom 24 px above the work-area bottom. Window: pill minus the inset.
        Assert.Equal((850 - inset, 976 - inset), (x, y));
        Assert.Equal(1040 - 24, y + inset + 40);
        Assert.Equal(1040 - 3, y + 40 + (2 * inset));

        // Same at 225% (inset 47 px, margin 54 px) on a secondary monitor left of the primary.
        var secondary = new RectPx(-3840, 0, 0, 2112);
        var (sx, sy) = OverlayPlacement.BottomCenter(secondary, 405 + 94, 90 + 94, 54, 47);
        Assert.Equal(-3840 + ((3840 - 405) / 2), sx + 47);
        Assert.Equal(2112 - 54, sy + 47 + 90);
    }

    [Fact]
    public void BottomCenter_WithShadowInset_KeepsThePillInsideAndMatchesTheNoInsetOverload()
    {
        var workArea = new RectPx(100, 50, 500, 650);

        // Zero inset equals the 4-argument overload.
        Assert.Equal(
            OverlayPlacement.BottomCenter(workArea, 300, 50, 24),
            OverlayPlacement.BottomCenter(workArea, 300, 50, 24, 0));

        // A pill wider than the work area aligns its visible left edge (not the shadow) to the edge.
        var (x, _) = OverlayPlacement.BottomCenter(workArea, 640 + 42, 50 + 42, 24, 21);
        Assert.Equal(100 - 21, x);

        // The work area is too short for the pill plus its margin: the pill (not the window) is clamped to the top.
        var (_, y) = OverlayPlacement.BottomCenter(new RectPx(0, 10, 800, 70), 200 + 42, 50 + 42, 24, 21);
        Assert.Equal(10 - 21, y);

        // A negative inset is treated as zero.
        Assert.Equal(
            OverlayPlacement.BottomCenter(workArea, 300, 50, 24),
            OverlayPlacement.BottomCenter(workArea, 300, 50, 24, -5));
    }

    [Fact]
    public void SelectTargetWindow_PrefersExternalForegroundThenLastExternalWindow()
    {
        var external = new IntPtr(0x1001);
        var lastExternal = new IntPtr(0x2002);

        // Typing in another app: that app's window decides the monitor.
        Assert.Equal(external, OverlayPlacement.SelectTargetWindow(external, foregroundIsOwnProcess: false, lastExternal));

        // MainWindow in front: the previous input target decides.
        Assert.Equal(lastExternal, OverlayPlacement.SelectTargetWindow(external, foregroundIsOwnProcess: true, lastExternal));

        // No foreground window.
        Assert.Equal(lastExternal, OverlayPlacement.SelectTargetWindow(IntPtr.Zero, foregroundIsOwnProcess: false, lastExternal));

        // Nothing known: zero, so the caller falls back to the cursor's monitor, then the primary.
        Assert.Equal(IntPtr.Zero, OverlayPlacement.SelectTargetWindow(external, foregroundIsOwnProcess: true, IntPtr.Zero));
        Assert.Equal(IntPtr.Zero, OverlayPlacement.SelectTargetWindow(IntPtr.Zero, foregroundIsOwnProcess: false, IntPtr.Zero));
    }

    [Theory]
    [InlineData(1.0, 24)]
    [InlineData(1.25, 30)]
    [InlineData(1.5, 36)]
    [InlineData(1.75, 42)]
    [InlineData(2.25, 54)]
    [InlineData(0.0, 24)]
    [InlineData(double.NaN, 24)]
    public void DipToPx_ConvertsTheBottomMarginAtTheMonitorScale(double scale, int expected)
    {
        Assert.Equal(24, OverlayPlacement.BottomMarginDip);
        Assert.Equal(expected, OverlayPlacement.DipToPx(OverlayPlacement.BottomMarginDip, scale));
    }

    [Fact]
    public void RectPx_ReportsWidthAndHeight()
    {
        var rect = new RectPx(-100, 20, 300, 80);

        Assert.Equal(400, rect.Width);
        Assert.Equal(60, rect.Height);
    }
}
