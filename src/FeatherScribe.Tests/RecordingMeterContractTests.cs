using System.Text.RegularExpressions;
using System.Xml.Linq;
using FeatherScribe.App;

namespace FeatherScribe.Tests;

/// <summary>
/// Phase UI-7 contracts: the volume-linked recording meter (visual_direction.md §24).
/// The level never enters Core, never blocks the audio thread, and is shown only in Recording.
/// </summary>
public sealed class RecordingMeterContractTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Core_AudioRecorderAndPipelineDoNotKnowAudioLevel()
    {
        var core = Path.Combine(FindRepoRoot(), "src", "FeatherScribe.Core");

        Assert.DoesNotContain("AudioLevel", File.ReadAllText(Path.Combine(core, "Interfaces.cs")));
        foreach (var path in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("AudioLevel", File.ReadAllText(path));
        }
    }

    [Fact]
    public void Recorder_RaisesLevelOutsideWriterLockInsideTryCatch()
    {
        var code = ReadInfrastructureFile("NAudioRecorder.cs");

        Assert.Contains("public event Action<float>? AudioLevelChanged;", code);
        Assert.DoesNotContain("IAudioRecorder.AudioLevel", code);

        // DataAvailable: the file write is unchanged and the level is raised after the lock.
        var handler = code[code.IndexOf("waveIn.DataAvailable +=", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("waveIn.RecordingStopped +=", StringComparison.Ordinal)];
        var lockBody = MethodBody(handler, "lock (writer)");
        Assert.Contains("writer.Write(e.Buffer, 0, e.BytesRecorded);", lockBody);
        Assert.DoesNotContain("RaiseAudioLevel", lockBody);
        Assert.True(
            handler.IndexOf("RaiseAudioLevel(e.Buffer, e.BytesRecorded);", StringComparison.Ordinal)
                > handler.IndexOf("writer.Write(e.Buffer", StringComparison.Ordinal));

        var raise = MethodBody(code, "private void RaiseAudioLevel(");
        AssertGuarded(raise);
        Assert.Contains("AudioLevelMeter.ComputeLevel(buffer, bytesRecorded)", raise);

        // After recording stops, 0 is raised once (guarded) from the finally block.
        var finallyBlock = MethodBody(code, "finally");
        Assert.Single(Regex.Matches(finallyBlock, @"RaiseAudioLevelStopped\(\);"));
        var stopped = MethodBody(code, "private void RaiseAudioLevelStopped()");
        AssertGuarded(stopped);
        Assert.Contains("AudioLevelChanged?.Invoke(0f);", stopped);

        // Only the scalar leaves the recorder; no samples or levels are logged or stored.
        Assert.DoesNotContain("Console.", code);
        Assert.DoesNotContain("EventLog", code);
    }

    [Fact]
    public void App_CoalescesLevelsWithBeginInvokeAndUnsubscribesOnExit()
    {
        var app = ReadAppFile("App.xaml.cs");

        Assert.Contains("_recorder = new NAudioRecorder(settings.Recording);", app);
        Assert.Contains("_recorder.AudioLevelChanged += OnAudioLevelChanged;", app);

        var onLevel = MethodBody(app, "private void OnAudioLevelChanged(float level)");
        Assert.Contains("Volatile.Write(ref _latestAudioLevel, level);", onLevel);
        Assert.Contains("if (Interlocked.Exchange(ref _levelUpdatePending, 1) == 0)", onLevel);
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Render,", onLevel);
        Assert.DoesNotContain("Dispatcher.Invoke(", onLevel);
        Assert.DoesNotContain("SetAudioLevel", onLevel);

        var apply = MethodBody(app, "private void ApplyLatestAudioLevel()");
        Assert.Contains("Volatile.Write(ref _levelUpdatePending, 0);", apply);
        Assert.Contains("_overlay?.SetAudioLevel(Volatile.Read(ref _latestAudioLevel));", apply);
        Assert.True(
            apply.IndexOf("_levelUpdatePending, 0", StringComparison.Ordinal)
                < apply.IndexOf("SetAudioLevel", StringComparison.Ordinal));

        // The level is applied only through the coalesced path, never through the pipeline stage handler.
        Assert.Single(Regex.Matches(app, @"SetAudioLevel\("));
        var stageHandler = app[app.IndexOf("pipeline.StageChanged +=", StringComparison.Ordinal)..];
        stageHandler = stageHandler[..stageHandler.IndexOf("});", StringComparison.Ordinal)];
        Assert.DoesNotContain("AudioLevel", stageHandler);

        var onExit = MethodBody(app, "protected override void OnExit(ExitEventArgs e)");
        Assert.Contains("_recorder.AudioLevelChanged -= OnAudioLevelChanged;", onExit);

        // No timer drives the meter.
        Assert.DoesNotContain("DispatcherTimer", app);
    }

    [Fact]
    public void Overlay_SetAudioLevelReturnsEarlyUnlessRecording()
    {
        var code = ReadAppFile("RecordingOverlay.xaml.cs");
        var body = MethodBody(code, "internal void SetAudioLevel(float level)");

        var guard = body.IndexOf("if (_currentState != OverlayVisualState.Recording)", StringComparison.Ordinal);
        Assert.True(guard >= 0);
        Assert.True(guard < body.IndexOf("return;", StringComparison.Ordinal));
        Assert.True(body.IndexOf("return;", StringComparison.Ordinal) < body.IndexOf("_levelSmoother.Next(level)", StringComparison.Ordinal));
        Assert.Contains(".Height = LevelBarHeight(", body);
        Assert.DoesNotContain("BeginAnimation", body);
        Assert.DoesNotContain("Storyboard", body);
    }

    [Fact]
    public void Overlay_MeterIsShownOnlyInRecordingAndResetWhenLeaving()
    {
        var code = ReadAppFile("RecordingOverlay.xaml.cs");

        var indicator = MethodBody(code, "private void ApplyIndicatorVisibility(OverlayVisualState state)");
        Assert.Contains(
            "LevelMeter.Visibility = state == OverlayVisualState.Recording\n            ? Visibility.Visible\n            : Visibility.Collapsed;",
            indicator.Replace("\r\n", "\n", StringComparison.Ordinal));

        var applyState = MethodBody(code, "private void ApplyStateVisuals(OverlayVisualState state)");
        Assert.Contains("ResetLevelMeter();", applyState);
        Assert.True(
            applyState.IndexOf("ResetLevelMeter();", StringComparison.Ordinal)
                < applyState.IndexOf("_currentState = state;", StringComparison.Ordinal));

        var hide = MethodBody(code, "public void HideStatus()");
        Assert.Contains("ResetLevelMeter();", hide);
        Assert.Contains("LevelMeter.Visibility = Visibility.Collapsed;", hide);

        var closed = MethodBody(code, "protected override void OnClosed(EventArgs e)");
        Assert.Contains("ResetLevelMeter();", closed);

        var reset = MethodBody(code, "private void ResetLevelMeter()");
        Assert.Contains("_levelSmoother.Reset();", reset);
        Assert.Contains("bar.Height = _levelBarMinHeight;", reset);
    }

    [Fact]
    public void Overlay_NoTimerOrStoryboardIsIntroducedForTheMeter()
    {
        // Code only: comments may mention what is intentionally not used.
        var code = string.Join(
            '\n',
            ReadAppFile("RecordingOverlay.xaml.cs")
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        var document = LoadOverlayXaml();

        // Still exactly the elapsed timer and the auto-hide timer.
        Assert.Equal(2, Regex.Matches(code, @"new DispatcherTimer\(").Count);
        Assert.Equal(2, Regex.Matches(code, @"\bDispatcherTimer\s+_\w+").Count);
        Assert.DoesNotContain("Storyboard", code);
        Assert.DoesNotContain("CompositionTarget", code);
        Assert.Empty(Regex.Matches(code, @"\b(LevelBar\d|LevelMeter|_levelBars\[i\]|bar)\.BeginAnimation"));

        var meter = FindElementByName(document, "LevelMeter")!;
        Assert.DoesNotContain(meter.DescendantsAndSelf(), element => element.Name.LocalName is "Storyboard" or "DoubleAnimation" or "BeginStoryboard");
    }

    [Fact]
    public void OverlayXaml_DefinesCollapsedMeterWithFourBarsBeforeElapsedText()
    {
        var document = LoadOverlayXaml();
        var meter = FindElementByName(document, "LevelMeter");

        Assert.NotNull(meter);
        Assert.Equal("StackPanel", meter.Name.LocalName);
        Assert.Equal("Horizontal", meter.Attribute("Orientation")?.Value);
        Assert.Equal("Center", meter.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("10,0,0,0", meter.Attribute("Margin")?.Value);
        Assert.Equal("Collapsed", meter.Attribute("Visibility")?.Value);
        // Fixed meter height: changing bar heights never changes the pill size.
        Assert.Equal("{StaticResource LevelMeterMaxBarHeight}", meter.Attribute("Height")?.Value);

        var bars = meter.Elements().ToArray();
        Assert.Equal(4, bars.Length);
        for (var i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];
            Assert.Equal("Rectangle", bar.Name.LocalName);
            Assert.Equal($"LevelBar{i + 1}", bar.Attribute(XamlNamespace + "Name")?.Value);
            Assert.Equal("3", bar.Attribute("Width")?.Value);
            Assert.Equal("1.5", bar.Attribute("RadiusX")?.Value);
            Assert.Equal("1.5", bar.Attribute("RadiusY")?.Value);
            Assert.Equal("Center", bar.Attribute("VerticalAlignment")?.Value);
            Assert.Equal("{StaticResource RecordingBrush}", bar.Attribute("Fill")?.Value);
            Assert.Equal("{StaticResource LevelMeterMinBarHeight}", bar.Attribute("Height")?.Value);
        }

        // The meter sits in the right column, directly before ElapsedText (name and style kept).
        var elapsed = FindElementByName(document, "ElapsedText");
        Assert.NotNull(elapsed);
        Assert.Same(meter.Parent, elapsed.Parent);
        Assert.Same(elapsed, meter.ElementsAfterSelf().First());
        Assert.Equal("2", meter.Parent!.Attribute("Grid.Column")?.Value);
        Assert.Equal("Horizontal", meter.Parent.Attribute("Orientation")?.Value);
        Assert.Equal("{StaticResource OverlayTimerTextStyle}", elapsed.Attribute("Style")?.Value);
        Assert.Equal("Collapsed", elapsed.Attribute("Visibility")?.Value);
    }

    [Fact]
    public void OverlayStates_CountIsUnchanged()
    {
        Assert.Equal(9, Enum.GetNames<OverlayVisualState>().Length);
        var states = LoadOverlayXaml()
            .Descendants()
            .Count(element => element.Name.LocalName == "VisualState");
        Assert.Equal(9, states);
    }

    [Fact]
    public void Spacing_DefinesMeterBarHeights()
    {
        var spacing = XDocument.Load(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", "Themes", "Spacing.xaml"));

        Assert.Equal("3", FindResourceValue(spacing, "LevelMeterMinBarHeight"));
        Assert.Equal("14", FindResourceValue(spacing, "LevelMeterMaxBarHeight"));
    }

    [Fact]
    public void LevelBarHeight_FollowsWeightedFormula()
    {
        Assert.Equal(new[] { 0.55, 1.0, 0.8, 0.45 }, RecordingOverlay.LevelBarWeights);

        foreach (var weight in RecordingOverlay.LevelBarWeights)
        {
            // Silence: every bar at the minimum height.
            Assert.Equal(3, RecordingOverlay.LevelBarHeight(0, weight, 3, 14));

            for (var smoothed = 0.0; smoothed <= 1.0; smoothed += 0.05)
            {
                Assert.InRange(RecordingOverlay.LevelBarHeight(smoothed, weight, 3, 14), 3, 14);
            }
        }

        // Full level: the weight-1.0 bar reaches the maximum; lighter bars stay below it.
        Assert.Equal(14, RecordingOverlay.LevelBarHeight(1, 1.0, 3, 14));
        Assert.Equal(3 + (11 * 0.55 * 1.25), RecordingOverlay.LevelBarHeight(1, 0.55, 3, 14), precision: 10);
        Assert.Equal(14, RecordingOverlay.LevelBarHeight(1, 0.8, 3, 14));
        Assert.Equal(3 + (11 * 0.45 * 1.25), RecordingOverlay.LevelBarHeight(1, 0.45, 3, 14), precision: 10);
        Assert.Equal(3 + (11 * 0.5 * 1.0 * 1.25), RecordingOverlay.LevelBarHeight(0.5, 1.0, 3, 14), precision: 10);
    }

    private static void AssertGuarded(string body)
    {
        Assert.Contains("try", body);
        Assert.Contains("catch (Exception)", body);
        Assert.DoesNotContain("throw", body);
        Assert.True(body.IndexOf("try", StringComparison.Ordinal) < body.IndexOf("AudioLevel", StringComparison.Ordinal));
    }

    private static string? FindResourceValue(XDocument document, string key)
        => document.Descendants()
            .FirstOrDefault(element => element.Attribute(XamlNamespace + "Key")?.Value == key)
            ?.Value
            .Trim();

    private static XElement? FindElementByName(XDocument document, string name)
        => document.Descendants()
            .FirstOrDefault(element => element.Attribute(XamlNamespace + "Name")?.Value == name);

    private static XDocument LoadOverlayXaml()
        => XDocument.Load(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", "RecordingOverlay.xaml"));

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

    private static string ReadAppFile(string fileName)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", fileName));

    private static string ReadInfrastructureFile(string fileName)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.Infrastructure", fileName));

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
