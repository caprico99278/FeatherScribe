namespace FeatherScribe.App;

/// <summary>A rectangle in physical (device) pixels, as returned by GetMonitorInfo / GetWindowRect.</summary>
internal readonly record struct RectPx(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>
/// Pure placement math for RecordingOverlay (visual_direction.md §25). No Win32 or WPF calls,
/// so monitor selection order and bottom-center clamping are unit-testable.
/// </summary>
internal static class OverlayPlacement
{
    /// <summary>Gap between the overlay bottom and the work-area bottom, in DIP.</summary>
    internal const double BottomMarginDip = 24;

    /// <summary>
    /// Top-left position that centers the overlay horizontally in the work area with its bottom
    /// <paramref name="marginPx"/> above the work-area bottom. The result is clamped so the whole
    /// overlay stays inside the work area; an overlay wider (or taller) than the work area is
    /// aligned to the left (or top) edge.
    /// </summary>
    internal static (int X, int Y) BottomCenter(RectPx workArea, int overlayWidthPx, int overlayHeightPx, int marginPx)
    {
        var width = Math.Max(0, overlayWidthPx);
        var height = Math.Max(0, overlayHeightPx);
        var margin = Math.Max(0, marginPx);

        var x = workArea.Left + ((workArea.Width - width) / 2);
        var y = workArea.Bottom - margin - height;

        return (Clamp(x, workArea.Left, workArea.Right - width), Clamp(y, workArea.Top, workArea.Bottom - height));
    }

    /// <summary>
    /// Same placement for a window that has <paramref name="insetPx"/> of transparent room on every
    /// side (the shadow inset): the math runs on the visible pill (window minus the inset), so the
    /// pill keeps <paramref name="marginPx"/> above the work-area bottom, stays centered and stays
    /// inside the work area. Returns the window's top-left, i.e. the pill position minus the inset.
    /// </summary>
    internal static (int X, int Y) BottomCenter(RectPx workArea, int windowWidthPx, int windowHeightPx, int marginPx, int insetPx)
    {
        var inset = Math.Max(0, insetPx);
        var (pillX, pillY) = BottomCenter(workArea, windowWidthPx - (2 * inset), windowHeightPx - (2 * inset), marginPx);
        return (pillX - inset, pillY - inset);
    }

    /// <summary>
    /// The window whose monitor hosts the overlay: the foreground window when it is not a
    /// FeatherScribe window, otherwise the tracker's last external window. Zero means "none":
    /// the caller then uses the monitor under the cursor, and finally the primary monitor.
    /// </summary>
    internal static IntPtr SelectTargetWindow(IntPtr foregroundWindow, bool foregroundIsOwnProcess, IntPtr lastExternalWindow)
    {
        if (foregroundWindow != IntPtr.Zero && !foregroundIsOwnProcess)
        {
            return foregroundWindow;
        }

        return lastExternalWindow;
    }

    /// <summary>Converts a DIP length to physical pixels at the given scale (1.0 = 96 DPI).</summary>
    internal static int DipToPx(double dip, double dpiScale)
        => (int)Math.Round(dip * (double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1), MidpointRounding.AwayFromZero);

    /// <summary>Lower bound wins, so an oversized overlay aligns to the left/top edge.</summary>
    private static int Clamp(int value, int minimum, int maximum)
        => Math.Max(minimum, Math.Min(value, maximum));
}
