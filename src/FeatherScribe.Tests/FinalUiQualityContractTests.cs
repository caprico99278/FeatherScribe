using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FeatherScribe.App;

namespace FeatherScribe.Tests;

/// <summary>
/// Phase UI-8 (Final UI Quality) contracts: WCAG contrast of the fixed palette, disabled buttons
/// without opacity, badge text, the window-scoped dark scrollbar, resource hygiene, and the
/// non-activating multi-monitor overlay placement (visual_direction.md §25).
/// </summary>
public sealed class FinalUiQualityContractTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] ThemeDictionaries =
    [
        "Colors.xaml",
        "Spacing.xaml",
        "Typography.xaml",
        "Motion.xaml",
        "Controls.xaml",
    ];

    // Public design tokens documented in visual_direction.md §17 (and required by
    // ThemeResourceTests) that production XAML/C# does not reference yet. They stay as the
    // documented token set; anything else that is never referenced must be removed.
    private static readonly string[] DocumentedUnreferencedTokens =
    [
        "Space4",
        "Space8",
        "Space12",
        "Space16",
        "Space24",
        "Space32",
        "Space40",
        "Space48",
        "Inset32",
        "FocusRingThickness",
        "SmallControlCornerRadius",
        "ResultTextStyle",
        "ButtonTextStyle",
        "MonospaceTextStyle",
        "DangerButtonStyle",
        "IconButtonStyle",
        "StatusPillStyle",
        "CardGroupBoxStyle",
    ];

    private static readonly Regex ResourceReferenceRegex = new(@"\{(?:StaticResource|DynamicResource)\s+([^},\s]+)", RegexOptions.Compiled);
    private static readonly Regex StringLiteralRegex = new("\"([A-Za-z][A-Za-z0-9]*)\"", RegexOptions.Compiled);
    private static readonly Regex HexColorRegex = new("#[0-9A-Fa-f]{3,8}\\b", RegexOptions.Compiled);

    // ---- 2.1 Contrast ----------------------------------------------------------------------

    [Theory]
    [InlineData("DisabledTextColor", "SurfaceColor", 4.5)]
    [InlineData("DisabledTextColor", "SurfaceElevatedColor", 4.5)]
    [InlineData("SecondaryTextColor", "SelectionColor", 4.5)]
    [InlineData("PrimaryTextColor", "SurfaceColor", 7.0)]
    [InlineData("WindowBackgroundColor", "AccentColor", 4.5)]
    [InlineData("WindowBackgroundColor", "AccentHoverColor", 4.5)]
    [InlineData("WindowBackgroundColor", "AccentPressedColor", 4.5)]
    [InlineData("WarningColor", "SurfaceColor", 4.5)]
    [InlineData("DangerColor", "SurfaceColor", 4.5)]
    [InlineData("FocusColor", "SurfaceColor", 3.0)]
    [InlineData("FocusColor", "SurfaceElevatedColor", 3.0)]
    // Scrollbar thumb at rest (MutedText) vs the surfaces it scrolls: non-text contrast >= 3:1.
    [InlineData("MutedTextColor", "SurfaceColor", 3.0)]
    [InlineData("MutedTextColor", "WindowBackgroundColor", 3.0)]
    // Captions outside badges stay muted: header caption on WindowBackground, guide text on Surface.
    [InlineData("MutedTextColor", "WindowBackgroundColor", 4.5)]
    [InlineData("MutedTextColor", "SurfaceColor", 4.3)]
    public void Palette_MeetsWcagContrast(string foregroundKey, string backgroundKey, double minimumRatio)
    {
        var colors = LoadColors();
        var ratio = ContrastRatio(colors[foregroundKey], colors[backgroundKey]);

        Assert.True(
            ratio >= minimumRatio,
            $"{foregroundKey} on {backgroundKey} is {ratio:0.00}:1, expected at least {minimumRatio}:1.");
    }

    [Fact]
    public void Contrast_MatchesMeasuredValues()
    {
        var colors = LoadColors();

        // Values reported in visual_direction.md §25 (WCAG 2.x relative luminance).
        Assert.Equal(6.60, ContrastRatio(colors["DisabledTextColor"], colors["SurfaceColor"]), 2);
        Assert.Equal(5.95, ContrastRatio(colors["DisabledTextColor"], colors["SurfaceElevatedColor"]), 2);
        Assert.Equal(5.05, ContrastRatio(colors["SecondaryTextColor"], colors["SelectionColor"]), 2);
        Assert.Equal(2.51, ContrastRatio(colors["MutedTextColor"], colors["SelectionColor"]), 2);
        Assert.Equal(4.30, ContrastRatio(colors["MutedTextColor"], colors["SurfaceColor"]), 2);
        Assert.Equal(4.69, ContrastRatio(colors["MutedTextColor"], colors["WindowBackgroundColor"]), 2);
        Assert.Equal(13.09, ContrastRatio(colors["PrimaryTextColor"], colors["SurfaceElevatedColor"]), 2);
    }

    [Theory]
    [InlineData("AppToolTipStyle", 4.5)]
    [InlineData("FeedbackSnackbarStyle", 4.5)]
    public void StyleTextOnStyleBackground_MeetsWcagContrast(string styleKey, double minimumRatio)
    {
        var controls = LoadThemeXaml("Controls.xaml");
        var style = FindResourceByKey(controls, styleKey);
        var background = SetterValue(style, "Background");
        var foreground = SetterValue(style, "Foreground") ?? (styleKey == "FeedbackSnackbarStyle"
            ? SetterValue(FindResourceByKey(controls, "FeedbackTextStyle"), "Foreground")
            : null);

        Assert.NotNull(background);
        Assert.NotNull(foreground);
        var ratio = ContrastRatio(ResolveBrushColor(foreground), ResolveBrushColor(background));
        Assert.True(ratio >= minimumRatio, $"{styleKey}: {foreground} on {background} is {ratio:0.00}:1.");
    }

    [Fact]
    public void DisabledText_IsClearlyDimmerThanSecondaryText()
    {
        var colors = LoadColors();

        Assert.True(
            ContrastRatio(colors["DisabledTextColor"], colors["SurfaceColor"])
                < ContrastRatio(colors["SecondaryTextColor"], colors["SurfaceColor"]) - 1.0);
        Assert.True(RelativeLuminance(colors["DisabledTextColor"]) < RelativeLuminance(colors["SecondaryTextColor"]));
        Assert.True(RelativeLuminance(colors["DisabledTextColor"]) > RelativeLuminance(colors["MutedTextColor"]));
    }

    [Theory]
    [InlineData("BaseButtonStyle")]
    [InlineData("PrimaryButtonStyle")]
    public void DisabledButtons_UseDisabledTextWithoutOpacityAndKeepFlatSurface(string styleKey)
    {
        var style = FindResourceByKey(LoadThemeXaml("Controls.xaml"), styleKey);
        var disabledTriggers = style
            .Descendants()
            .Where(element => element.Name.LocalName == "Trigger"
                && element.Attribute("Property")?.Value == "IsEnabled"
                && element.Attribute("Value")?.Value == "False")
            .ToArray();

        var trigger = Assert.Single(disabledTriggers);
        var setters = trigger.Elements().Where(element => element.Name.LocalName == "Setter").ToArray();

        Assert.DoesNotContain(setters, setter => setter.Attribute("Property")?.Value == "Opacity");
        Assert.Contains(setters, setter => setter.Attribute("Property")?.Value == "Foreground"
            && setter.Attribute("Value")?.Value == "{StaticResource DisabledTextBrush}");
        Assert.Contains(setters, setter => setter.Attribute("TargetName")?.Value == "ButtonBorder"
            && setter.Attribute("Property")?.Value == "Background"
            && setter.Attribute("Value")?.Value == "{StaticResource SurfaceBrush}");
        Assert.Contains(setters, setter => setter.Attribute("TargetName")?.Value == "ButtonBorder"
            && setter.Attribute("Property")?.Value == "BorderBrush"
            && setter.Attribute("Value")?.Value == "{StaticResource BorderBrush}");

        // The disabled trigger is the last one, so hover/press can never override it.
        Assert.Same(trigger, style.Descendants().Last(element => element.Name.LocalName == "Trigger"));
    }

    [Fact]
    public void Controls_NoButtonUsesOpacityOrMutedTextForDisabledState()
    {
        var controls = LoadThemeXaml("Controls.xaml");

        Assert.DoesNotContain(
            controls.Descendants(),
            element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Opacity"
                && element.Attribute("Value")?.Value == "0.65");
        Assert.DoesNotContain(
            controls.Descendants().Where(element => element.Name.LocalName == "Trigger"
                && element.Attribute("Property")?.Value == "IsEnabled"),
            trigger => trigger.Elements().Any(setter => setter.Attribute("Value")?.Value == "{StaticResource MutedTextBrush}"));
    }

    [Fact]
    public void BadgeText_UsesSecondaryTextAndCaptionStaysMuted()
    {
        var typography = LoadThemeXaml("Typography.xaml");
        var badge = FindResourceByKey(typography, "BadgeTextStyle");

        Assert.Equal("{x:Type TextBlock}", badge.Attribute("TargetType")?.Value);
        Assert.Equal("{StaticResource CaptionTextStyle}", badge.Attribute("BasedOn")?.Value);
        AssertSetter(badge, "Foreground", "{StaticResource SecondaryTextBrush}");
        AssertSetter(FindResourceByKey(typography, "CaptionTextStyle"), "Foreground", "{StaticResource MutedTextBrush}");

        // Every TextBlock directly inside a SubtleBadgeStyle border uses the badge text style.
        var badges = LoadAppXaml("MainWindow.xaml")
            .Descendants()
            .Where(element => element.Name.LocalName == "Border"
                && element.Attribute("Style")?.Value == "{StaticResource SubtleBadgeStyle}")
            .ToArray();
        Assert.Equal(2, badges.Length);
        var texts = badges.SelectMany(border => border.Elements()).ToArray();
        Assert.Equal(new[] { "状態", "コピー・貼り付け対象" }, texts.Select(text => text.Attribute("Text")?.Value));
        Assert.All(texts, text => Assert.Equal("{StaticResource BadgeTextStyle}", text.Attribute("Style")?.Value));
    }

    // ---- 2.2 Scrollbar ---------------------------------------------------------------------

    [Fact]
    public void DarkScrollBar_IsThinRoundedAndKeepsTrackPaging()
    {
        var controls = LoadThemeXaml("Controls.xaml");
        var spacing = LoadThemeXaml("Spacing.xaml");
        var scrollBar = FindResourceByKey(controls, "DarkScrollBarStyle");
        var thumb = FindResourceByKey(controls, "DarkScrollBarThumbStyle");
        var pageButton = FindResourceByKey(controls, "DarkScrollBarPageButtonStyle");

        Assert.Equal("8", FindResourceByKey(spacing, "ScrollBarSize").Value.Trim());
        Assert.Equal("3", FindResourceByKey(spacing, "ScrollBarThumbCornerRadius").Value.Trim());

        Assert.Equal("{x:Type ScrollBar}", scrollBar.Attribute("TargetType")?.Value);
        AssertSetter(scrollBar, "OverridesDefaultStyle", "True");
        AssertSetter(scrollBar, "Width", "{StaticResource ScrollBarSize}");
        AssertSetter(scrollBar, "Background", "Transparent");

        // Vertical (default) and horizontal (Orientation trigger) templates share the same parts.
        var orientation = Assert.Single(
            scrollBar.Descendants(),
            element => element.Name.LocalName == "Trigger" && element.Attribute("Property")?.Value == "Orientation");
        Assert.Equal("Horizontal", orientation.Attribute("Value")?.Value);
        Assert.Contains(orientation.Elements(), setter => setter.Attribute("Property")?.Value == "Height"
            && setter.Attribute("Value")?.Value == "{StaticResource ScrollBarSize}");

        var tracks = scrollBar.Descendants().Where(element => element.Name.LocalName == "Track").ToArray();
        Assert.Equal(2, tracks.Length);
        Assert.All(tracks, track => Assert.Equal("PART_Track", track.Attribute(XamlNamespace + "Name")?.Value));
        Assert.Equal("True", tracks[0].Attribute("IsDirectionReversed")?.Value);

        var commands = scrollBar.Descendants()
            .Where(element => element.Name.LocalName == "RepeatButton")
            .Select(element => element.Attribute("Command")?.Value)
            .ToArray();
        Assert.Equal(
            new[] { "ScrollBar.PageUpCommand", "ScrollBar.PageDownCommand", "ScrollBar.PageLeftCommand", "ScrollBar.PageRightCommand" },
            commands);
        Assert.All(
            scrollBar.Descendants().Where(element => element.Name.LocalName == "RepeatButton"),
            button => Assert.Equal("{StaticResource DarkScrollBarPageButtonStyle}", button.Attribute("Style")?.Value));
        Assert.All(
            scrollBar.Descendants().Where(element => element.Name.LocalName == "Thumb"),
            element => Assert.Equal("{StaticResource DarkScrollBarThumbStyle}", element.Attribute("Style")?.Value));

        // No arrow buttons: only the two page halves per orientation.
        Assert.Equal(4, scrollBar.Descendants().Count(element => element.Name.LocalName == "RepeatButton"));
        Assert.DoesNotContain(scrollBar.Descendants(), element => element.Name.LocalName == "Path");

        // Transparent page halves stay hit-testable and never take focus.
        AssertSetter(pageButton, "Background", "Transparent");
        AssertSetter(pageButton, "Focusable", "False");
        AssertSetter(pageButton, "IsTabStop", "False");

        // Thumb: rounded, MutedText at rest (>= 3:1 non-text contrast), SecondaryText on hover,
        // PrimaryText while dragging.
        AssertSetter(thumb, "Focusable", "False");
        var thumbBorder = thumb.Descendants().Single(element => element.Attribute(XamlNamespace + "Name")?.Value == "ThumbBorder");
        Assert.Equal("{StaticResource MutedTextBrush}", thumbBorder.Attribute("Background")?.Value);
        Assert.Equal("{StaticResource ScrollBarThumbCornerRadius}", thumbBorder.Attribute("CornerRadius")?.Value);
        var triggers = thumb.Descendants().Where(element => element.Name.LocalName == "Trigger").ToArray();
        Assert.Equal(new[] { "IsMouseOver", "IsDragging" }, triggers.Select(trigger => trigger.Attribute("Property")?.Value));
        Assert.Equal("{StaticResource SecondaryTextBrush}", triggers[0].Elements().Single().Attribute("Value")?.Value);
        Assert.Equal("{StaticResource PrimaryTextBrush}", triggers[1].Elements().Single().Attribute("Value")?.Value);
    }

    // ---- 2.4 Resource hygiene --------------------------------------------------------------

    [Fact]
    public void ProductionXaml_HasNoHardCodedColorsOutsideColorsDictionary()
    {
        var violations = new List<string>();
        foreach (var (name, document) in ProductionXaml().Where(item => item.Name != "Themes/Colors.xaml"))
        {
            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes())
                {
                    if (HexColorRegex.IsMatch(attribute.Value))
                    {
                        violations.Add($"{name}: <{element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\">");
                    }
                }

                foreach (var text in element.Nodes().OfType<XText>())
                {
                    if (HexColorRegex.IsMatch(text.Value))
                    {
                        violations.Add($"{name}: <{element.Name.LocalName}>{text.Value.Trim()}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));

        // Both shadows take their color from the palette.
        var controls = LoadThemeXaml("Controls.xaml");
        foreach (var key in new[] { "OverlayShadowEffect", "FeedbackShadowEffect" })
        {
            Assert.Equal("{StaticResource ShadowColor}", FindResourceByKey(controls, key).Attribute("Color")?.Value);
        }
    }

    [Fact]
    public void ThemeDictionaries_ContainNoImplicitStylesAndProductionXamlHasOnlyTheScrollBarOne()
    {
        foreach (var file in ThemeDictionaries)
        {
            foreach (var resource in LoadThemeXaml(file).Root!.Elements())
            {
                Assert.True(
                    resource.Attribute(XamlNamespace + "Key") is not null,
                    $"Themes/{file}: <{resource.Name.LocalName}> has no x:Key.");
            }
        }

        // Implicit styles = Style elements without x:Key directly inside a resources block.
        var implicitStyles = ProductionXaml()
            .SelectMany(item => item.Document.Descendants()
                .Where(element => element.Name.LocalName.EndsWith("Resources", StringComparison.Ordinal))
                .SelectMany(block => block.Elements())
                .Where(element => element.Name.LocalName == "Style" && element.Attribute(XamlNamespace + "Key") is null)
                .Select(element => $"{item.Name}:{element.Attribute("TargetType")?.Value}"))
            .ToArray();

        Assert.Equal(new[] { "MainWindow.xaml:{x:Type ScrollBar}", "MainWindow.xaml:{x:Type ToolTip}" }, implicitStyles);
    }

    [Fact]
    public void ThemeResources_AreReferencedOrDocumentedTokens()
    {
        var keys = ThemeDictionaries
            .SelectMany(file => LoadThemeXaml(file).Root!.Elements())
            .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
            .OfType<string>()
            .ToArray();
        var referenced = ReferencedResourceKeys();

        var unreferenced = keys.Where(key => !referenced.Contains(key)).ToArray();
        var unexpected = unreferenced.Except(DocumentedUnreferencedTokens).ToArray();
        Assert.True(unexpected.Length == 0, "Unreferenced keyed resources: " + string.Join(", ", unexpected));

        // The allow-list stays exact: every entry exists and is still unreferenced.
        Assert.All(DocumentedUnreferencedTokens, token => Assert.Contains(token, keys));
        Assert.Equal(DocumentedUnreferencedTokens.Order(), unreferenced.Order());

        // ...and each allow-listed token is documented in visual_direction.md §17.
        var doc = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "ui", "visual_direction.md"));
        var section17 = doc[doc.IndexOf("## 17. WPF Resource Mapping", StringComparison.Ordinal)..doc.IndexOf("## 18.", StringComparison.Ordinal)];
        Assert.All(DocumentedUnreferencedTokens, token => Assert.Contains($"`{token}`", section17));
    }

    [Fact]
    public void ProductionXaml_DoesNotRepeatTokenValuesAsLiterals()
    {
        var spacing = LoadThemeXaml("Spacing.xaml");
        var motion = LoadThemeXaml("Motion.xaml");
        var thicknessTokens = TokenValues(spacing, "Thickness", NormalizeThickness);
        var cornerRadiusTokens = TokenValues(spacing, "CornerRadius", NormalizeThickness);
        var durationTokens = TokenValues(motion, "Duration", value => TimeSpan.Parse(value, CultureInfo.InvariantCulture).ToString());

        var violations = new List<string>();
        foreach (var (name, document) in ProductionXaml())
        {
            foreach (var (property, value) in LiteralPropertyValues(document))
            {
                var tokens = property switch
                {
                    "Padding" or "BorderThickness" => thicknessTokens,
                    "CornerRadius" => cornerRadiusTokens,
                    "Duration" => durationTokens,
                    _ => null,
                };
                if (tokens is null)
                {
                    continue;
                }

                var normalized = property == "Duration"
                    ? TimeSpan.Parse(value, CultureInfo.InvariantCulture).ToString()
                    : NormalizeThickness(value);
                if (tokens.TryGetValue(normalized, out var token))
                {
                    violations.Add($"{name}: {property}=\"{value}\" equals token {token}");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    // ---- 2.3 Overlay placement (code contract) ---------------------------------------------

    [Fact]
    public void Overlay_IsPlacedWithNonActivatingSetWindowPosOnEntranceOnly()
    {
        var code = ReadAppFile("RecordingOverlay.xaml.cs");
        var codeLines = string.Join('\n', code.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // Never activates, never takes focus, never touches the foreground window.
        Assert.DoesNotContain("SetForegroundWindow", codeLines);
        Assert.DoesNotContain("Activate()", codeLines);
        Assert.DoesNotContain(".Focus(", codeLines);
        Assert.DoesNotContain("SystemParameters.WorkArea", codeLines);
        Assert.DoesNotMatch(new Regex(@"^\s*(Left|Top)\s*=", RegexOptions.Multiline), codeLines);

        Assert.Contains("private const uint SwpNoActivate = 0x0010;", code);
        var calls = Regex.Matches(code, @"(?<!extern bool )SetWindowPos\(handle[^;]*;").Select(match => match.Value).ToArray();
        Assert.Single(calls);
        Assert.All(calls, call => Assert.Contains("SwpNoSize | SwpNoZOrder | SwpNoActivate", call));

        // The overlay window styles are unchanged.
        Assert.Contains("styles | NoActivateStyle | TransparentStyle", code);
        Assert.Contains("private const int NoActivateStyle = 0x08000000;", code);
        Assert.Contains("private const int TransparentStyle = 0x00000020;", code);

        // Monitor selection runs only on entrance; visible state updates keep the same anchor.
        var show = MethodBody(code, "internal void ShowPresentation(OverlayPresentation presentation)");
        Assert.Single(Regex.Matches(code, @"PlaceOnInputTargetMonitor\(\);"));
        var elseIndex = show.IndexOf("else", show.IndexOf("if (wasVisible)", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(show.IndexOf("PlaceOnInputTargetMonitor();", StringComparison.Ordinal) > elseIndex);
        Assert.True(show.IndexOf("KeepAnchoredOnPlacementMonitor();", StringComparison.Ordinal) < elseIndex);
        Assert.DoesNotContain("TryGetInputTargetWorkArea", MethodBody(code, "private void KeepAnchoredOnPlacementMonitor()"));

        var select = MethodBody(code, "private bool TryGetInputTargetWorkArea(out RectPx workArea)");
        Assert.Contains("OverlayPlacement.SelectTargetWindow(", select);
        Assert.Contains("_foregroundWindowTracker.LastExternalWindow", select);
        Assert.Contains("MonitorFromWindow(target, MonitorDefaultToNearest)", select);
        Assert.Contains("MonitorFromPoint(cursor, MonitorDefaultToNearest)", select);
        Assert.Contains("MonitorFromPoint(default, MonitorDefaultToPrimary)", select);
        Assert.Contains("GetMonitorInfo(monitor, ref info)", select);
        Assert.Contains("info.WorkArea", select);

        // Place twice: the second pass corrects a DPI change applied by WPF on the first move.
        var place = MethodBody(code, "private void PlaceOnInputTargetMonitor()");
        Assert.Equal(2, Regex.Matches(place, @"MoveToBottomCenter\(handle, workArea\);").Count);
        var move = MethodBody(code, "private void MoveToBottomCenter(IntPtr handle, RectPx workArea)");
        Assert.Contains("UpdateLayout();", move);
        Assert.Contains("GetWindowRect(handle, out var bounds)", move);
        Assert.Contains("var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;", move);
        Assert.Contains("OverlayPlacement.DipToPx(OverlayPlacement.BottomMarginDip, scale)", move);
        Assert.Contains("OverlayPlacement.DipToPx(OverlayRoot.Margin.Bottom, scale)", move);
        Assert.Contains("OverlayPlacement.BottomCenter(workArea, current.Width, current.Height, marginPx, insetPx)", move);
    }

    [Fact]
    public void Overlay_ShadowInsetFullyContainsTheShadowAndStaysClickThrough()
    {
        var spacing = LoadThemeXaml("Spacing.xaml");
        var controls = LoadThemeXaml("Controls.xaml");
        var overlay = LoadAppXaml("RecordingOverlay.xaml");

        // Uniform inset, so the visible pill stays centered inside the window.
        var inset = double.Parse(FindResourceByKey(spacing, "OverlayShadowInset").Value.Trim(), CultureInfo.InvariantCulture);
        var shadow = FindResourceByKey(controls, "OverlayShadowEffect");
        var blur = double.Parse(shadow.Attribute("BlurRadius")!.Value, CultureInfo.InvariantCulture);
        var depth = double.Parse(shadow.Attribute("ShadowDepth")!.Value, CultureInfo.InvariantCulture);

        // Smallest whole DIP that contains the blur plus the full offset in any direction.
        Assert.Equal(Math.Ceiling(blur + depth), inset);
        Assert.Equal(21, inset);
        Assert.True(inset < OverlayPlacement.BottomMarginDip, "The pill margin must be larger than the transparent inset.");

        var root = overlay.Descendants().Single(element => element.Attribute(XamlNamespace + "Name")?.Value == "OverlayRoot");
        Assert.Equal("{StaticResource OverlayShadowInset}", root.Attribute("Margin")?.Value);
        Assert.Equal("False", root.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("{StaticResource OverlayShellStyle}", overlay.Descendants()
            .Single(element => element.Attribute(XamlNamespace + "Name")?.Value == "OverlayShell").Attribute("Style")?.Value);

        // Still a transparent, click-through, non-activating window.
        var window = overlay.Root!;
        Assert.Equal("Transparent", window.Attribute("Background")?.Value);
        Assert.Equal("True", window.Attribute("AllowsTransparency")?.Value);
        Assert.Equal("False", window.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("False", window.Attribute("ShowActivated")?.Value);
        Assert.Contains("styles | NoActivateStyle | TransparentStyle", ReadAppFile("RecordingOverlay.xaml.cs"));
    }


    [Fact]
    public void Overlay_ReadsTheAppOwnedTrackerWithoutChangingIt()
    {
        var app = ReadAppFile("App.xaml.cs");
        var tracker = ReadAppFile("ForegroundWindowTracker.cs");
        var overlay = ReadAppFile("RecordingOverlay.xaml.cs");

        Assert.Contains("_overlay = new RecordingOverlay(foregroundWindowTracker);", app);
        Assert.Single(Regex.Matches(app, @"new ForegroundWindowTracker\("));
        Assert.Contains("internal IntPtr LastExternalWindow => _lastExternalWindow;", tracker);
        // Only the tracker's own capture assigns it (comparisons "==" do not count).
        Assert.Single(Regex.Matches(tracker, @"_lastExternalWindow\s*=(?!=)"));
        Assert.DoesNotContain("TryRestoreLastExternalWindow", overlay);
        Assert.DoesNotContain("new ForegroundWindowTracker", overlay);
        Assert.DoesNotContain(".Dispose()", MethodBody(overlay, "protected override void OnClosed(EventArgs e)"));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static Dictionary<string, string> TokenValues(XDocument document, string elementName, Func<string, string> normalize)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in document.Root!.Elements().Where(element => element.Name.LocalName == elementName))
        {
            tokens.TryAdd(normalize(element.Value.Trim()), element.Attribute(XamlNamespace + "Key")!.Value);
        }

        return tokens;
    }

    /// <summary>Literal (non-markup-extension) values of attributes and Setter Property/Value pairs.</summary>
    private static IEnumerable<(string Property, string Value)> LiteralPropertyValues(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            if (element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value is { } property
                && element.Attribute("Value")?.Value is { } setterValue
                && !setterValue.StartsWith('{'))
            {
                yield return (property, setterValue);
            }

            foreach (var attribute in element.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration && !attribute.Value.StartsWith('{'))
                {
                    yield return (attribute.Name.LocalName, attribute.Value);
                }
            }
        }
    }

    private static string NormalizeThickness(string value)
    {
        var parts = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN)
            .ToArray();
        var expanded = parts.Length switch
        {
            1 => new[] { parts[0], parts[0], parts[0], parts[0] },
            2 => new[] { parts[0], parts[1], parts[0], parts[1] },
            4 => parts,
            _ => [double.NaN],
        };
        return string.Join(",", expanded.Select(part => part.ToString(CultureInfo.InvariantCulture)));
    }

    private static HashSet<string> ReferencedResourceKeys()
    {
        var appDirectory = Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App");
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, document) in ProductionXaml())
        {
            foreach (var attribute in document.Descendants().Attributes())
            {
                foreach (Match match in ResourceReferenceRegex.Matches(attribute.Value))
                {
                    referenced.Add(match.Groups[1].Value);
                }
            }
        }

        // C# looks resources up by string key (FindResource / TryFindResource, key tables).
        foreach (var path in Directory.GetFiles(appDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            foreach (Match match in StringLiteralRegex.Matches(File.ReadAllText(path)))
            {
                referenced.Add(match.Groups[1].Value);
            }
        }

        return referenced;
    }

    private static IEnumerable<(string Name, XDocument Document)> ProductionXaml()
    {
        var appDirectory = Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App");
        foreach (var path in Directory.GetFiles(appDirectory, "*.xaml", SearchOption.TopDirectoryOnly).Order())
        {
            yield return (Path.GetFileName(path), XDocument.Load(path));
        }

        foreach (var file in ThemeDictionaries)
        {
            yield return ($"Themes/{file}", LoadThemeXaml(file));
        }
    }

    private static string? SetterValue(XElement style, string property)
        => style.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Setter" && element.Attribute("Property")?.Value == property)
            ?.Attribute("Value")?.Value;

    /// <summary>"{StaticResource XBrush}" to the brush's #RRGGBB through its Color resource in Colors.xaml.</summary>
    private static string ResolveBrushColor(string reference)
    {
        var brushKey = ResourceReferenceRegex.Match(reference).Groups[1].Value;
        var brush = FindResourceByKey(LoadThemeXaml("Colors.xaml"), brushKey);
        var colorKey = ResourceReferenceRegex.Match(brush.Attribute("Color")!.Value).Groups[1].Value;
        return LoadColors()[colorKey];
    }

    private static Dictionary<string, string> LoadColors()
        => LoadThemeXaml("Colors.xaml")
            .Root!
            .Elements()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(element => element.Attribute(XamlNamespace + "Key")!.Value, element => element.Value.Trim());

    internal static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        var a = RelativeLuminance(foregroundHex);
        var b = RelativeLuminance(backgroundHex);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>WCAG 2.x relative luminance of an opaque #RRGGBB color (an #AARRGGBB alpha is rejected).</summary>
    private static double RelativeLuminance(string hex)
    {
        var digits = hex.TrimStart('#');
        Assert.True(digits.Length == 6, $"Contrast is only defined here for opaque colors: {hex}");

        static double Channel(string component)
        {
            var value = int.Parse(component, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(digits[..2])) + (0.7152 * Channel(digits[2..4])) + (0.0722 * Channel(digits[4..6]));
    }

    private static XElement FindResourceByKey(XDocument document, string key)
        => document.Descendants().FirstOrDefault(element => element.Attribute(XamlNamespace + "Key")?.Value == key)
            ?? throw new InvalidOperationException($"Missing resource: {key}");

    private static void AssertSetter(XElement style, string property, string value)
        => Assert.Contains(
            style.Elements(),
            element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == property
                && element.Attribute("Value")?.Value == value);

    private static string MethodBody(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var open = code.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{')
            {
                depth++;
            }
            else if (code[i] == '}' && --depth == 0)
            {
                return code[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body: {signature}");
    }

    private static XDocument LoadThemeXaml(string fileName)
        => XDocument.Load(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", "Themes", fileName));

    private static XDocument LoadAppXaml(string fileName)
        => XDocument.Load(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", fileName));

    private static string ReadAppFile(string fileName)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", fileName));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FeatherScribe.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FeatherScribe repository root.");
    }
}
