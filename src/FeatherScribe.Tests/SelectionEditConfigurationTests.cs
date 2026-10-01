using System.Text;
using System.Text.Json.Nodes;
using FeatherScribe.App;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

/// <summary>
/// Phase UX-1 settings, the extra action hotkey, user-facing texts and overlay mapping of selected-text editing.
/// </summary>
public sealed class SelectionEditConfigurationTests : IDisposable
{
    private readonly string _rootPath;

    public SelectionEditConfigurationTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "FeatherScribeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "config"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootPath, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // --- Settings ---

    [Fact]
    public void Defaults_EditSelectionHotkeyAndPoliteMode()
    {
        var settings = new AppSettings();

        Assert.Equal("Ctrl+Shift+F7", settings.Hotkeys.EditSelection);
        Assert.Equal("Polite", settings.SelectionEdit.Mode);
        Assert.Equal(FormattingMode.Polite, settings.SelectionEdit.ParsedMode);
        Assert.Equal(600, settings.SelectionEdit.CaptureTimeoutMilliseconds);
        Assert.False(settings.Llm.Enabled); // inert until the user enables LLM formatting
    }

    [Fact]
    public void DefaultEditSelectionHotkey_Parses()
    {
        Assert.True(HotkeyParser.TryParse(new HotkeySettings().EditSelection, out var definition));
        Assert.Equal(0x76u, definition.VirtualKey); // F7
        Assert.Equal(
            HotkeyParser.Modifiers.Control | HotkeyParser.Modifiers.Shift,
            definition.Modifiers & (HotkeyParser.Modifiers.Control | HotkeyParser.Modifiers.Shift));
    }

    [Theory]
    [InlineData("Polite", true, FormattingMode.Polite)]
    [InlineData("bullet", true, FormattingMode.Bullet)]
    [InlineData(" Memo ", true, FormattingMode.Memo)]
    [InlineData("PlainQuality", true, FormattingMode.PlainQuality)]
    [InlineData("DevInstruction", true, FormattingMode.DevInstruction)]
    [InlineData("NoFormat", false, FormattingMode.Polite)]
    [InlineData("Teleport", false, FormattingMode.Polite)]
    [InlineData("3", false, FormattingMode.Polite)]
    [InlineData("PlainFast,Polite", false, FormattingMode.Polite)]
    [InlineData("", false, FormattingMode.Polite)]
    [InlineData(null, false, FormattingMode.Polite)]
    public void TryParseMode(string? text, bool valid, FormattingMode expected)
    {
        Assert.Equal(valid, SelectionEditSettings.TryParseMode(text, out var mode));
        Assert.Equal(expected, mode);
        Assert.Equal(expected, new SelectionEditSettings { Mode = text! }.ParsedMode);
    }

    [Fact]
    public void Load_MissingBlock_UsesDefaultsWithoutWarning()
    {
        WriteSettings("""{ "llm": { "enabled": true } }""");

        var provider = new JsonAppSettingsProvider(_rootPath);
        var settings = provider.Load();

        Assert.Equal("Ctrl+Shift+F7", settings.Hotkeys.EditSelection);
        Assert.Equal(FormattingMode.Polite, settings.SelectionEdit.ParsedMode);
        Assert.Equal(600, settings.SelectionEdit.CaptureTimeoutMilliseconds);
        Assert.Empty(provider.LastWarnings);
    }

    [Fact]
    public void Load_ValidBlock_ParsesModeTimeoutAndHotkey()
    {
        WriteSettings("""
        {
          "hotkeys": { "editSelection": "Ctrl+Alt+E" },
          "selectionEdit": { "mode": "memo", "captureTimeoutMilliseconds": 900 }
        }
        """);

        var provider = new JsonAppSettingsProvider(_rootPath);
        var settings = provider.Load();

        Assert.Equal("Ctrl+Alt+E", settings.Hotkeys.EditSelection);
        Assert.Equal("Ctrl+Shift+F8", settings.Hotkeys.NoFormat); // other hotkeys keep their defaults
        Assert.Equal(FormattingMode.Memo, settings.SelectionEdit.ParsedMode);
        Assert.Equal(900, settings.SelectionEdit.CaptureTimeoutMilliseconds);
        Assert.Empty(provider.LastWarnings);
    }

    [Theory]
    [InlineData("NoFormat")]
    [InlineData("Teleport")]
    public void Load_NoFormatOrInvalidMode_FallsBackToPoliteWithWarning(string mode)
    {
        WriteSettings($$"""{ "selectionEdit": { "mode": "{{mode}}" } }""");

        var provider = new JsonAppSettingsProvider(_rootPath);
        var settings = provider.Load();

        Assert.Equal("Polite", settings.SelectionEdit.Mode);
        Assert.Equal(FormattingMode.Polite, settings.SelectionEdit.ParsedMode);
        var warning = Assert.Single(provider.LastWarnings);
        Assert.Contains("selectionEdit.mode", warning, StringComparison.Ordinal);
        Assert.Contains(mode, warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10, 100)]
    [InlineData(60000, 5000)]
    public void Load_OutOfRangeCaptureTimeout_IsClampedWithWarning(int configured, int expected)
    {
        WriteSettings($$"""{ "selectionEdit": { "captureTimeoutMilliseconds": {{configured}} } }""");

        var provider = new JsonAppSettingsProvider(_rootPath);
        var settings = provider.Load();

        Assert.Equal(expected, settings.SelectionEdit.CaptureTimeoutMilliseconds);
        Assert.Contains(provider.LastWarnings, warning => warning.Contains("captureTimeoutMilliseconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_NullBlock_UsesDefaults()
    {
        WriteSettings("""{ "selectionEdit": null }""");

        var settings = new JsonAppSettingsProvider(_rootPath).Load();

        Assert.Equal(FormattingMode.Polite, settings.SelectionEdit.ParsedMode);
    }

    [Fact]
    public void ShippedConfig_HasEditSelectionHotkeyAndPoliteMode_LlmStaysOff()
    {
        var json = File.ReadAllText(Path.Combine(FindRepoRoot(), "config", "appsettings.json"));
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("Ctrl+Shift+F7", (string?)root["hotkeys"]!["editSelection"]);
        Assert.Equal("Polite", (string?)root["selectionEdit"]!["mode"]);
        Assert.False((bool)root["llm"]!["enabled"]!);
    }

    // --- Extra action hotkey ---

    private sealed class FakeRegistrar(Func<int, HotkeyDefinition, (bool Success, int Win32Error)>? handler = null)
        : IHotkeyRegistrar
    {
        public List<(int Id, HotkeyDefinition Definition)> Calls { get; } = [];

        public bool TryRegister(int id, HotkeyDefinition definition, out int win32Error)
        {
            Calls.Add((id, definition));
            var (success, error) = handler?.Invoke(id, definition) ?? (true, 0);
            win32Error = error;
            return success;
        }
    }

    [Fact]
    public void RegisterAction_Success_UsesActionIdAndParser()
    {
        var registrar = new FakeRegistrar();

        var registration = HotkeyActionRegistration.Register(
            registrar, HotkeyActionRegistration.FirstActionId, UserFacingText.SelectionEditActionLabel, "Ctrl+Shift+F7");

        Assert.True(registration.IsRegistered);
        Assert.False(registration.Failed);
        Assert.Equal(HotkeyActionRegistration.FirstActionId, registration.Id);
        var call = Assert.Single(registrar.Calls);
        Assert.Equal(HotkeyActionRegistration.FirstActionId, call.Id);
        Assert.Equal(0x76u, call.Definition.VirtualKey);
    }

    [Fact]
    public void RegisterAction_IdsNeverCollideWithModeIds()
    {
        var registrar = new FakeRegistrar();
        var report = HotkeyRegistrationPlanner.RegisterAll(new HotkeySettings(), registrar);

        Assert.All(report.Registered, registration => Assert.True(registration.Id < HotkeyActionRegistration.FirstActionId));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HotkeyActionRegistration.Register(registrar, 7, "x", "Ctrl+Shift+F7"));
    }

    [Theory]
    [InlineData(HotkeyRegistrationPlanner.ErrorHotkeyAlreadyRegistered, "他アプリが登録済み (Win32Error=1409)")]
    [InlineData(5, "Win32Error=5")]
    public void RegisterAction_RegistrarFailure_IsReported(int win32Error, string expectedReason)
    {
        var registrar = new FakeRegistrar((_, _) => (false, win32Error));

        var registration = HotkeyActionRegistration.Register(
            registrar, HotkeyActionRegistration.FirstActionId, UserFacingText.SelectionEditActionLabel, "Ctrl+Shift+F7");

        Assert.True(registration.Failed);
        Assert.False(registration.IsRegistered);
        Assert.Equal(expectedReason, registration.FailureReason);
        Assert.Equal(
            $"⚠ Ctrl+Shift+F7（選択テキスト編集）は使えません: {expectedReason}",
            UserFacingText.HotkeyFailureLine(registration));
    }

    [Fact]
    public void RegisterAction_InvalidText_IsParseFailureWithoutRegistrarCall()
    {
        var registrar = new FakeRegistrar();

        var registration = HotkeyActionRegistration.Register(
            registrar, HotkeyActionRegistration.FirstActionId, UserFacingText.SelectionEditActionLabel, "NotAHotkey+++");

        Assert.Equal("書式不正", registration.FailureReason);
        Assert.Empty(registrar.Calls);
    }

    [Fact]
    public void RegisterAction_RegistrarThrows_IsReportedNotThrown()
    {
        var registrar = new FakeRegistrar((_, _) => throw new InvalidOperationException("boom"));

        var registration = HotkeyActionRegistration.Register(
            registrar, HotkeyActionRegistration.FirstActionId, UserFacingText.SelectionEditActionLabel, "Ctrl+Shift+F7");

        Assert.Equal("例外: boom", registration.FailureReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void RegisterAction_EmptyText_IsDisabledNotFailure(string? text)
    {
        var registrar = new FakeRegistrar();

        var registration = HotkeyActionRegistration.Register(
            registrar, HotkeyActionRegistration.FirstActionId, UserFacingText.SelectionEditActionLabel, text);

        Assert.True(registration.IsDisabled);
        Assert.False(registration.Failed);
        Assert.False(registration.IsRegistered);
        Assert.Empty(registrar.Calls);
    }

    // --- User-facing text and overlay ---

    [Theory]
    [InlineData(SelectionEditStatus.Replaced, "選択範囲を置き換えました")]
    [InlineData(SelectionEditStatus.LlmDisabled, "LLM整形がオフのため、選択テキストの編集は使えません")]
    [InlineData(SelectionEditStatus.Busy, "処理中のため、選択テキストの編集を開始できません")]
    [InlineData(SelectionEditStatus.CaptureFailed, "選択テキストを取得できませんでした")]
    [InlineData(SelectionEditStatus.EditFailed, "編集できなかったため、選択テキストは変更していません")]
    [InlineData(SelectionEditStatus.TargetChanged, "入力先が変わったため置き換えませんでした。編集結果はクリップボードにあります")]
    [InlineData(SelectionEditStatus.ReplaceFailed, "選択範囲を置き換えできませんでした")]
    public void Notice_IsExact(SelectionEditStatus status, string expected)
    {
        Assert.Equal(expected, UserFacingText.SelectionEditNotice(status));
    }

    [Fact]
    public void Notice_CoversEveryStatus()
    {
        var notices = Enum.GetValues<SelectionEditStatus>().Select(UserFacingText.SelectionEditNotice).ToList();

        Assert.Equal(notices.Count, notices.Distinct().Count());
    }

    [Theory]
    [InlineData(SelectionEditStatus.Replaced, "Completed", 1000)]
    [InlineData(SelectionEditStatus.ReplaceFailed, "Failed", 2200)]
    [InlineData(SelectionEditStatus.TargetChanged, "Warning", 2200)]
    [InlineData(SelectionEditStatus.EditFailed, "Warning", 1800)]
    [InlineData(SelectionEditStatus.CaptureFailed, "Warning", 1800)]
    [InlineData(SelectionEditStatus.LlmDisabled, "Warning", 1800)]
    [InlineData(SelectionEditStatus.Busy, "Warning", 1800)]
    public void Overlay_OutcomeIsTemporaryWithNotice(SelectionEditStatus status, string state, int delayMilliseconds)
    {
        var outcome = new SelectionEditOutcome(status, "reason", UserFacingText.SelectionEditNotice(status), 0);

        var presentation = OverlayPresentationMapper.FromSelectionEditOutcome(outcome);

        Assert.Equal(state, presentation.State.ToString());
        Assert.Equal(UserFacingText.SelectionEditNotice(status), presentation.Text);
        Assert.False(presentation.IsPersistent);
        Assert.False(presentation.ShowsElapsed);
        Assert.Equal(TimeSpan.FromMilliseconds(delayMilliseconds), presentation.AutoHideDelay);
    }

    [Fact]
    public void Overlay_FormattingIsPersistent()
    {
        var presentation = OverlayPresentationMapper.FromSelectionEditFormatting();

        Assert.Equal(OverlayVisualState.Formatting, presentation.State);
        Assert.True(presentation.IsPersistent);
        Assert.Equal("整形中", presentation.Text);
    }

    [Theory]
    [InlineData(SelectionEditStatus.TargetChanged, true)]
    [InlineData(SelectionEditStatus.EditFailed, true)]
    [InlineData(SelectionEditStatus.ReplaceFailed, true)]
    [InlineData(SelectionEditStatus.Replaced, false)]
    [InlineData(SelectionEditStatus.CaptureFailed, false)]
    [InlineData(SelectionEditStatus.Busy, false)]
    [InlineData(SelectionEditStatus.LlmDisabled, false)]
    public void TrayNotice_OnlyWhenTheUserHasSomethingToActOn(SelectionEditStatus status, bool expected)
    {
        var outcome = new SelectionEditOutcome(status, "reason", UserFacingText.SelectionEditNotice(status), 0);

        var notice = UserFacingText.SelectionEditTrayNotice(outcome);

        Assert.Equal(expected, notice is not null);
        if (notice is not null)
        {
            Assert.Equal("選択テキストの編集", notice.Title);
            Assert.Equal(outcome.Notice, notice.Body);
        }
    }

    [Fact]
    public void GuideLine_ShowsHotkeyAndModeLabel()
    {
        Assert.Equal(
            "Ctrl+Shift+F7　選択テキストを編集（丁寧文）",
            UserFacingText.SelectionEditGuideLine("Ctrl+Shift+F7", FormattingMode.Polite));
        Assert.Equal(
            "Ctrl+Alt+E　選択テキストを編集（箇条書き）",
            UserFacingText.SelectionEditGuideLine("Ctrl+Alt+E", FormattingMode.Bullet));
    }

    [Fact]
    public void OperationGuide_OmitsDisabledSelectionEditHotkey()
    {
        var guide = UserFacingText.OperationGuide(
            new HotkeySettings { EditSelection = "" }, llmEnabled: true, FormattingMode.Polite);

        Assert.DoesNotContain("選択テキストを編集", guide);
        Assert.Equal(9, guide.Split('\n').Length);
    }

    // --- DictationController.IsBusy ---

    [Fact]
    public async Task DictationController_IsBusyWhileRecordingOrProcessing()
    {
        var settings = new AppSettings();
        var speech = new GatedSpeechToText();
        var pipeline = new DictationPipeline(
            new InstantRecorder(),
            speech,
            new PassThroughFormatter(),
            new DictionaryCorrector([]),
            new NullOutput(),
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);
        var controller = new DictationController(pipeline, settings);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.Completed += _ => completed.TrySetResult();

        Assert.False(controller.IsBusy);
        controller.Toggle(FormattingMode.NoFormat);
        Assert.True(controller.IsBusy);
        await speech.Started.WaitAsync(TimeSpan.FromSeconds(5));
        controller.Toggle(FormattingMode.NoFormat); // stop → Processing
        Assert.True(controller.IsBusy);
        speech.Release();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(controller.IsBusy);
    }

    private void WriteSettings(string json)
        => File.WriteAllText(Path.Combine(_rootPath, "config", "appsettings.json"), json, Encoding.UTF8);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FeatherScribe.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class InstantRecorder : IAudioRecorder
    {
        public Task<RecordedAudio> RecordUntilStoppedAsync(CancellationToken cancellationToken)
        {
            var path = Path.Combine(Path.GetTempPath(), $"fs_selection_edit_test_{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, [0x00]);
            return Task.FromResult(new RecordedAudio(
                new AudioFile(path, TimeSpan.FromSeconds(1)),
                DateTimeOffset.Now,
                DateTimeOffset.Now));
        }
    }

    private sealed class GatedSpeechToText : ISpeechToTextEngine
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async Task<TranscriptionResult> TranscribeAsync(AudioFile audioFile, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new TranscriptionResult("text", TimeSpan.Zero, true, null);
        }
    }

    private sealed class PassThroughFormatter : ITextFormatter
    {
        public Task<FormatResult> FormatAsync(FormatRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new FormatResult(request.RawText, false, null));
    }

    private sealed class NullOutput : ITextOutput
    {
        public Task OutputAsync(string text, OutputMode mode, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyDictionaryProvider : IDictionaryProvider
    {
        public IReadOnlyList<DictionaryEntry> Load() => [];
    }

    private sealed class NullEventLog : IEventLog
    {
        public void Write(PipelineEvent entry)
        {
        }
    }
}
