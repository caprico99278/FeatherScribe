using System.Text.RegularExpressions;
using System.Xml.Linq;
using FeatherScribe.App;

namespace FeatherScribe.Tests;

public sealed class ThemeResourceTests
{
    private static readonly string[] ExpectedDictionaryOrder =
    [
        "Themes/Colors.xaml",
        "Themes/Spacing.xaml",
        "Themes/Typography.xaml",
        "Themes/Motion.xaml",
        "Themes/Controls.xaml",
    ];

    private static readonly string[] RequiredColorKeys =
    [
        "WindowBackgroundColor",
        "SurfaceColor",
        "SurfaceElevatedColor",
        "SurfaceHoverColor",
        "BorderColor",
        "BorderStrongColor",
        "PrimaryTextColor",
        "SecondaryTextColor",
        "MutedTextColor",
        "AccentColor",
        "AccentHoverColor",
        "AccentPressedColor",
        "RecordingColor",
        "SuccessColor",
        "WarningColor",
        "DangerColor",
        "OverlayBackgroundColor",
        "SelectionColor",
        "FocusColor",
    ];

    private static readonly string[] RequiredBrushKeys =
    [
        "WindowBackgroundBrush",
        "SurfaceBrush",
        "SurfaceElevatedBrush",
        "SurfaceHoverBrush",
        "BorderBrush",
        "BorderStrongBrush",
        "PrimaryTextBrush",
        "SecondaryTextBrush",
        "MutedTextBrush",
        "AccentBrush",
        "AccentHoverBrush",
        "AccentPressedBrush",
        "RecordingBrush",
        "SuccessBrush",
        "WarningBrush",
        "DangerBrush",
        "OverlayBackgroundBrush",
        "SelectionBrush",
        "FocusBrush",
    ];

    private static readonly string[] RequiredSpacingKeys =
    [
        "Space4",
        "Space8",
        "Space12",
        "Space16",
        "Space24",
        "Space32",
        "Space40",
        "Space48",
        "Inset4",
        "Inset8",
        "Inset12",
        "Inset16",
        "Inset24",
        "Inset32",
        "ButtonPadding",
        "OverlayPadding",
        "SmallControlCornerRadius",
        "ButtonCornerRadius",
        "CardCornerRadius",
        "OverlayCornerRadius",
        "PillCornerRadius",
        "FocusRingThickness",
        "StandardBorderThickness",
    ];

    private static readonly string[] RequiredTypographyKeys =
    [
        "AppFontFamily",
        "ResultFontFamily",
        "MonospaceFontFamily",
        "AppTitleTextStyle",
        "PageTitleTextStyle",
        "SectionTitleTextStyle",
        "BodyTextStyle",
        "ResultTextStyle",
        "ButtonTextStyle",
        "CaptionTextStyle",
        "StatusTextStyle",
        "MonospaceTextStyle",
    ];

    private static readonly string[] RequiredMotionKeys =
    [
        "MotionFastDuration",
        "MotionNormalDuration",
        "MotionSlowDuration",
        "MotionEaseOut",
        "MotionEaseInOut",
    ];

    private static readonly string[] RequiredControlStyleKeys =
    [
        "KeyboardFocusVisualStyle",
        "BaseButtonStyle",
        "PrimaryButtonStyle",
        "SecondaryButtonStyle",
        "GhostButtonStyle",
        "DangerButtonStyle",
        "IconButtonStyle",
        "CardBorderStyle",
        "ResultCardStyle",
        "StatusPillStyle",
        "CardGroupBoxStyle",
        "ReadOnlyTextBoxStyle",
        "ResultTextBoxStyle",
        "AppToolTipStyle",
        "MainStatusBarStyle",
        "SectionExpanderStyle",
        "ExpanderHeaderTextStyle",
        "EmptyStateTextStyle",
        "SubtleBadgeStyle",
        "OverlayShellStyle",
        "OverlayPrimaryTextStyle",
        "OverlayTimerTextStyle",
        "OverlayShadowEffect",
    ];

    private static readonly Regex StaticResourceRegex = new(@"\{StaticResource\s+([^},\s]+)", RegexOptions.Compiled);
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void App_MergesThemeDictionariesInRequiredOrder()
    {
        var sources = LoadXaml("src", "FeatherScribe.App", "App.xaml")
            .Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary")
            .Select(element => element.Attribute("Source")?.Value)
            .Where(value => value is not null)
            .ToArray();

        Assert.Equal(ExpectedDictionaryOrder, sources);
    }

    [Fact]
    public void ThemeDictionaries_DefineRequiredResourceKeys()
    {
        var resources = LoadThemeResourceCatalog();
        var requiredKeys = RequiredColorKeys
            .Concat(RequiredBrushKeys)
            .Concat(RequiredSpacingKeys)
            .Concat(RequiredTypographyKeys)
            .Concat(RequiredMotionKeys)
            .Concat(RequiredControlStyleKeys);

        foreach (var key in requiredKeys)
        {
            Assert.Contains(key, resources.Keys);
        }
    }

    [Fact]
    public void ThemeDictionaries_UseVisualDirectionPalette()
    {
        var resources = LoadThemeResourceCatalog();

        AssertColor(resources, "WindowBackgroundColor", "#101418");
        AssertColor(resources, "SurfaceColor", "#171D23");
        AssertColor(resources, "SurfaceElevatedColor", "#1E262D");
        AssertColor(resources, "SurfaceHoverColor", "#26313A");
        AssertColor(resources, "BorderColor", "#2A343D");
        AssertColor(resources, "BorderStrongColor", "#3A4853");
        AssertColor(resources, "PrimaryTextColor", "#E8EEF2");
        AssertColor(resources, "SecondaryTextColor", "#AEBBC5");
        AssertColor(resources, "MutedTextColor", "#74828D");
        AssertColor(resources, "AccentColor", "#7FD8D2");
        AssertColor(resources, "AccentHoverColor", "#95E4DE");
        AssertColor(resources, "AccentPressedColor", "#5DBCB7");
        AssertColor(resources, "RecordingColor", "#E06A6A");
        AssertColor(resources, "SuccessColor", "#7ACB8A");
        AssertColor(resources, "WarningColor", "#E2B76B");
        AssertColor(resources, "DangerColor", "#E07A7A");
        AssertColor(resources, "OverlayBackgroundColor", "#D91A222A");
        AssertColor(resources, "SelectionColor", "#28484C");
        AssertColor(resources, "FocusColor", "#9CEBE6");
    }

    [Theory]
    [InlineData("src", "FeatherScribe.App", "MainWindow.xaml")]
    [InlineData("src", "FeatherScribe.App", "RecordingOverlay.xaml")]
    public void ProductionXaml_StaticResourceReferencesExist(params string[] pathParts)
    {
        var resources = LoadThemeResourceCatalog();
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), Path.Combine(pathParts)));
        var references = StaticResourceRegex
            .Matches(text)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();

        Assert.NotEmpty(references);

        foreach (var key in references)
        {
            Assert.Contains(key, resources.Keys);
        }
    }

    [Fact]
    public void MainWindow_PreservesRequiredNamedControlsAndClickHandlers()
    {
        var document = LoadMainWindowXaml();
        var requiredNames = new[]
        {
            "StatusText",
            "HotkeyHelpText",
            "LastResultText",
            "RejectedResultText",
            "ReformatButton",
            "AdoptRejectedButton",
            "RecopyButton",
            "RepasteButton",
        };

        foreach (var name in requiredNames)
        {
            Assert.NotNull(FindElementByName(document, name));
        }

        AssertButtonContract(document, "ReformatButton", "ReformatButton_Click", "GhostButtonStyle");
        AssertButtonContract(document, "AdoptRejectedButton", "AdoptRejectedButton_Click", "SecondaryButtonStyle");
        AssertButtonContract(document, "RecopyButton", "RecopyButton_Click", "SecondaryButtonStyle");
        AssertButtonContract(document, "RepasteButton", "RepasteButton_Click", "PrimaryButtonStyle");
    }

    [Fact]
    public void MainWindow_UsesRequestedWindowSizingAndLayoutResources()
    {
        var document = LoadMainWindowXaml();
        var window = document.Root ?? throw new InvalidOperationException("MainWindow root element is missing.");

        Assert.Equal("800", window.Attribute("Width")?.Value);
        Assert.Equal("540", window.Attribute("Height")?.Value);
        Assert.Equal("720", window.Attribute("MinWidth")?.Value);
        Assert.Equal("480", window.Attribute("MinHeight")?.Value);
        Assert.Equal("CenterScreen", window.Attribute("WindowStartupLocation")?.Value);

        var scrollViewer = window.Elements().First(element => element.Name.LocalName == "ScrollViewer");
        Assert.Equal("Auto", scrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", scrollViewer.Attribute("HorizontalScrollBarVisibility")?.Value);

        var rootGrid = scrollViewer.Elements().First(element => element.Name.LocalName == "Grid");
        Assert.Equal("{StaticResource Inset24}", rootGrid.Attribute("Margin")?.Value);
    }

    [Fact]
    public void MainWindow_PlacesResultCandidateAndHotkeyControlsInExpectedRegions()
    {
        var document = LoadMainWindowXaml();
        var lastResult = FindElementByName(document, "LastResultText")!;
        var rejectedResult = FindElementByName(document, "RejectedResultText")!;
        var adoptRejected = FindElementByName(document, "AdoptRejectedButton")!;
        var hotkeyHelp = FindElementByName(document, "HotkeyHelpText")!;

        AssertAncestorStyle(lastResult, "Border", "ResultCardStyle");

        var rejectedExpander = AssertAncestorStyle(rejectedResult, "Expander", "SectionExpanderStyle");
        Assert.Same(rejectedExpander, adoptRejected.Ancestors().First(element => element.Name.LocalName == "Expander"));

        AssertAncestorStyle(hotkeyHelp, "Expander", "SectionExpanderStyle");
    }

    [Fact]
    public void MainWindow_DefinesEmptyStateWithoutReplacingResultControls()
    {
        var document = LoadMainWindowXaml();
        var emptyStateTexts = document
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Where(element => element.Attribute("Text")?.Value.Contains("ホットキー") == true
                || element.Attribute("Text")?.Value.Contains("候補がある場合") == true)
            .ToArray();

        Assert.Equal(2, emptyStateTexts.Length);

        foreach (var text in emptyStateTexts)
        {
            Assert.Contains(
                text.Descendants(),
                element => element.Name.LocalName == "DataTrigger"
                    && element.Attribute("Value")?.Value == "");
        }
    }

    [Fact]
    public void RecordingOverlay_PreservesNonActivatingWindowContract()
    {
        var window = LoadRecordingOverlayXaml().Root
            ?? throw new InvalidOperationException("RecordingOverlay root element is missing.");

        Assert.Equal("None", window.Attribute("WindowStyle")?.Value);
        Assert.Equal("True", window.Attribute("AllowsTransparency")?.Value);
        Assert.Equal("Transparent", window.Attribute("Background")?.Value);
        Assert.Equal("True", window.Attribute("Topmost")?.Value);
        Assert.Equal("False", window.Attribute("ShowInTaskbar")?.Value);
        Assert.Equal("False", window.Attribute("ShowActivated")?.Value);
        Assert.Equal("WidthAndHeight", window.Attribute("SizeToContent")?.Value);
        Assert.Equal("NoResize", window.Attribute("ResizeMode")?.Value);
        Assert.Equal("True", window.Attribute("UseLayoutRounding")?.Value);
        Assert.Equal("True", window.Attribute("SnapsToDevicePixels")?.Value);
        Assert.Equal("False", window.Attribute("Focusable")?.Value);
        Assert.Equal("False", window.Attribute("IsHitTestVisible")?.Value);
    }

    [Fact]
    public void RecordingOverlay_DefinesRequiredNamedElements()
    {
        var document = LoadRecordingOverlayXaml();
        var requiredNames = new[]
        {
            "OverlayRoot",
            "OverlayShell",
            "IndicatorDot",
            "StateGlyph",
            "TranscribingDots",
            "TranscribingDot1",
            "TranscribingDot2",
            "TranscribingDot3",
            "FormattingSweep",
            "FormattingSweepTranslate",
            "PastingArrow",
            "PastingArrowTranslate",
            "OverlayText",
            "ElapsedText",
        };

        foreach (var name in requiredNames)
        {
            Assert.NotNull(FindElementByName(document, name));
        }

        Assert.Equal(
            "{StaticResource OverlayShellStyle}",
            FindElementByName(document, "OverlayShell")?.Attribute("Style")?.Value);
        Assert.Equal(
            "{StaticResource OverlayPrimaryTextStyle}",
            FindElementByName(document, "OverlayText")?.Attribute("Style")?.Value);
        Assert.Equal(
            "{StaticResource OverlayTimerTextStyle}",
            FindElementByName(document, "ElapsedText")?.Attribute("Style")?.Value);
    }

    [Fact]
    public void RecordingOverlay_DefinesAllOverlayVisualStates()
    {
        var states = LoadRecordingOverlayXaml()
            .Descendants()
            .Where(element => element.Name.LocalName == "VisualState")
            .Select(element => element.Attribute(XamlNamespace + "Name")?.Value)
            .Where(value => value is not null)
            .ToArray();

        Assert.Equal(
            Enum.GetNames<OverlayVisualState>(),
            states);
    }

    [Fact]
    public void RecordingOverlay_UsesDedicatedProcessingIndicatorsAndSingleHideOwner()
    {
        var document = LoadRecordingOverlayXaml();
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", "RecordingOverlay.xaml"));
        var code = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", "RecordingOverlay.xaml.cs"));
        var hiddenState = document
            .Descendants()
            .Single(element => element.Name.LocalName == "VisualState"
                && element.Attribute(XamlNamespace + "Name")?.Value == "Hidden");

        Assert.Contains("MotionNormalDuration", code);
        Assert.NotNull(FindElementByName(document, "TranscribingDots"));
        Assert.NotNull(FindElementByName(document, "FormattingSweep"));
        Assert.NotNull(FindElementByName(document, "PastingArrow"));
        Assert.DoesNotContain(hiddenState.Descendants(), element => element.Name.LocalName == "Storyboard");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "Image");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "BitmapImage");
    }

    private static ThemeResourceCatalog LoadThemeResourceCatalog()
    {
        var root = FindRepoRoot();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var source in ExpectedDictionaryOrder)
        {
            var path = Path.Combine(root, "src", "FeatherScribe.App", source);
            var document = XDocument.Load(path);

            foreach (var element in document.Descendants())
            {
                var key = element.Attribute(XamlNamespace + "Key")?.Value;
                if (key is null)
                {
                    continue;
                }

                keys.Add(key);
                if (element.Name.LocalName == "Color")
                {
                    colors[key] = element.Value.Trim();
                }
            }
        }

        return new ThemeResourceCatalog(keys, colors);
    }

    private static void AssertColor(ThemeResourceCatalog resources, string key, string expected)
    {
        Assert.True(resources.Colors.TryGetValue(key, out var actual), $"Missing color resource: {key}");
        Assert.Equal(expected, actual, ignoreCase: true);
    }

    private static XDocument LoadXaml(params string[] pathParts)
    {
        return XDocument.Load(Path.Combine(FindRepoRoot(), Path.Combine(pathParts)));
    }

    private static XDocument LoadMainWindowXaml()
    {
        return LoadXaml("src", "FeatherScribe.App", "MainWindow.xaml");
    }

    private static XDocument LoadRecordingOverlayXaml()
    {
        return LoadXaml("src", "FeatherScribe.App", "RecordingOverlay.xaml");
    }

    private static XElement? FindElementByName(XDocument document, string name)
    {
        return document
            .Descendants()
            .FirstOrDefault(element => element.Attribute(XamlNamespace + "Name")?.Value == name);
    }

    private static void AssertButtonContract(XDocument document, string name, string clickHandler, string styleKey)
    {
        var button = FindElementByName(document, name) ?? throw new InvalidOperationException($"Missing button: {name}");

        Assert.Equal("Button", button.Name.LocalName);
        Assert.Equal(clickHandler, button.Attribute("Click")?.Value);
        Assert.Equal("False", button.Attribute("IsEnabled")?.Value);
        Assert.NotNull(button.Attribute("ToolTip"));
        Assert.Equal($"{{StaticResource {styleKey}}}", button.Attribute("Style")?.Value);
    }

    private static XElement AssertAncestorStyle(XElement element, string ancestorName, string styleKey)
    {
        var ancestor = element
            .Ancestors()
            .FirstOrDefault(candidate => candidate.Name.LocalName == ancestorName
                && candidate.Attribute("Style")?.Value == $"{{StaticResource {styleKey}}}");

        Assert.NotNull(ancestor);
        return ancestor;
    }

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

    private sealed record ThemeResourceCatalog(HashSet<string> Keys, Dictionary<string, string> Colors);
}
