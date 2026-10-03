using FeatherScribe.App;
using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public sealed class PasteTargetGuardTextOutputTests
{
    private sealed class RecordingOutput : ITextOutput
    {
        public List<(string Text, OutputMode Mode)> Calls { get; } = [];

        public Task OutputAsync(string text, OutputMode mode, CancellationToken cancellationToken)
        {
            Calls.Add((text, mode));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExternalForeground_DelegatesClipboardAndPasteOnce()
    {
        var inner = new RecordingOutput();
        var restoreCalls = 0;
        var guard = new PasteTargetGuardTextOutput(inner, () => false, () => { restoreCalls++; return true; });

        await guard.OutputAsync("text", OutputMode.ClipboardAndPaste, CancellationToken.None);

        Assert.Equal([("text", OutputMode.ClipboardAndPaste)], inner.Calls);
        Assert.Equal(0, restoreCalls);
    }

    [Fact]
    public async Task OwnForeground_RestoreSucceeds_DelegatesClipboardAndPaste()
    {
        var inner = new RecordingOutput();
        var restoreCalls = 0;
        var guard = new PasteTargetGuardTextOutput(inner, () => true, () => { restoreCalls++; return true; });

        await guard.OutputAsync("text", OutputMode.ClipboardAndPaste, CancellationToken.None);

        Assert.Equal([("text", OutputMode.ClipboardAndPaste)], inner.Calls);
        Assert.Equal(1, restoreCalls);
    }

    [Fact]
    public async Task OwnForeground_RestoreFails_CopiesOnlyThenThrows()
    {
        var inner = new RecordingOutput();
        var guard = new PasteTargetGuardTextOutput(inner, () => true, () => false);

        var exception = await Assert.ThrowsAsync<PasteTargetUnavailableException>(
            () => guard.OutputAsync("text", OutputMode.ClipboardAndPaste, CancellationToken.None));

        Assert.Equal("paste target unavailable", exception.Message);
        // The text stays on the clipboard; Ctrl+V is never sent into FeatherScribe's own window.
        Assert.Equal([("text", OutputMode.ClipboardOnly)], inner.Calls);
    }

    [Fact]
    public async Task ClipboardOnly_DelegatesUnchangedWithoutForegroundChecks()
    {
        var inner = new RecordingOutput();
        var foregroundChecks = 0;
        var restoreCalls = 0;
        var guard = new PasteTargetGuardTextOutput(
            inner,
            () => { foregroundChecks++; return true; },
            () => { restoreCalls++; return false; });

        await guard.OutputAsync("text", OutputMode.ClipboardOnly, CancellationToken.None);

        Assert.Equal([("text", OutputMode.ClipboardOnly)], inner.Calls);
        Assert.Equal(0, foregroundChecks);
        Assert.Equal(0, restoreCalls);
    }

    [Fact]
    public async Task PipelineReportsOutputFailure_WhenPasteTargetIsUnavailable()
    {
        var settings = new AppSettings();
        var inner = new RecordingOutput();
        using var pipeline = new DictationPipeline(
            new FakeRecorder(),
            new FixedSpeechToText("hello"),
            new UnusedFormatter(),
            new DictionaryCorrector([]),
            new PasteTargetGuardTextOutput(inner, () => true, () => false),
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);

        var result = await pipeline.RunAsync(FormattingMode.NoFormat, CancellationToken.None, CancellationToken.None);

        Assert.Equal(OutputMode.ClipboardAndPaste, settings.Output.ParsedMode);
        Assert.True(result.Success);
        Assert.Equal("hello", result.Text);
        Assert.False(result.OutputSucceeded);
        Assert.Equal([("hello", OutputMode.ClipboardOnly)], inner.Calls);
    }

    [Theory]
    // mode, llm enabled (raw-first): every output path carries the paste guard error separately.
    [InlineData(FormattingMode.NoFormat, false)]
    [InlineData(FormattingMode.PlainFast, false)]
    [InlineData(FormattingMode.PlainFast, true)]
    public async Task PasteTargetUnavailable_UsesClipboardWordingAndKeepsEventLogErrorType(
        FormattingMode mode,
        bool llmEnabled)
    {
        var settings = new AppSettings { Llm = new LlmSettings { Enabled = llmEnabled, RawFirstPaste = true } };
        var eventLog = new CapturingEventLog();
        using var pipeline = new DictationPipeline(
            new FakeRecorder(),
            new FixedSpeechToText("hello"),
            new FailingFormatter(),
            new DictionaryCorrector([]),
            new PasteTargetGuardTextOutput(new RecordingOutput(), () => true, () => false),
            new EmptyDictionaryProvider(),
            eventLog,
            settings);

        var result = await pipeline.RunAsync(mode, CancellationToken.None, CancellationToken.None);

        Assert.False(result.OutputSucceeded);
        Assert.Equal("paste target unavailable", result.OutputErrorMessage);
        var output = Assert.Single(eventLog.Events, entry => entry.Stage == "output");
        Assert.False(output.Success);
        Assert.Equal("paste target unavailable", output.ErrorType);
        Assert.Equal("貼り付け先が見つからないため、クリップボードにコピーしました", UserFacingText.ForResult(result));
        Assert.Equal(
            new TrayNotice("貼り付けできませんでした", "貼り付け先が見つからないため、クリップボードにコピーしました。"),
            UserFacingText.CompletionNotice(result));
    }

    [Fact]
    public async Task OtherOutputFailure_KeepsGenericWordingAndEventLogErrorType()
    {
        var settings = new AppSettings();
        var eventLog = new CapturingEventLog();
        using var pipeline = new DictationPipeline(
            new FakeRecorder(),
            new FixedSpeechToText("hello"),
            new FailingFormatter(),
            new DictionaryCorrector([]),
            new ThrowingOutput(),
            new EmptyDictionaryProvider(),
            eventLog,
            settings);

        var result = await pipeline.RunAsync(FormattingMode.NoFormat, CancellationToken.None, CancellationToken.None);

        Assert.False(result.OutputSucceeded);
        Assert.Equal("SendInput failed", result.OutputErrorMessage);
        Assert.Equal("SendInput failed", Assert.Single(eventLog.Events, entry => entry.Stage == "output").ErrorType);
        Assert.Equal("貼り付けできませんでした・結果は画面に保持しています", UserFacingText.ForResult(result));
        Assert.Equal(
            new TrayNotice("貼り付けできませんでした", "結果は画面に保持しています。画面からクリップボードにコピーできます。"),
            UserFacingText.CompletionNotice(result));
    }

    private sealed class FailingFormatter : ITextFormatter
    {
        public Task<FormatResult> FormatAsync(FormatRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new FormatResult(request.RawText, true, "LLM へ接続できません"));
    }

    private sealed class ThrowingOutput : ITextOutput
    {
        public Task OutputAsync(string text, OutputMode mode, CancellationToken cancellationToken)
            => throw new InvalidOperationException("SendInput failed");
    }

    private sealed class CapturingEventLog : IEventLog
    {
        private readonly object _gate = new();
        private readonly List<PipelineEvent> _events = [];

        public IReadOnlyList<PipelineEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return [.. _events];
                }
            }
        }

        public void Write(PipelineEvent entry)
        {
            lock (_gate)
            {
                _events.Add(entry);
            }
        }
    }

    private sealed class FakeRecorder : IAudioRecorder
    {
        public Task<RecordedAudio> RecordUntilStoppedAsync(CancellationToken cancellationToken)
        {
            var path = Path.Combine(Path.GetTempPath(), $"fs_guard_test_{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, [0x00]);
            return Task.FromResult(new RecordedAudio(
                new AudioFile(path, TimeSpan.FromSeconds(1)),
                DateTimeOffset.Now,
                DateTimeOffset.Now));
        }
    }

    private sealed class FixedSpeechToText(string text) : ISpeechToTextEngine
    {
        public Task<TranscriptionResult> TranscribeAsync(AudioFile audioFile, CancellationToken cancellationToken)
            => Task.FromResult(new TranscriptionResult(text, TimeSpan.Zero, true, null));
    }

    private sealed class UnusedFormatter : ITextFormatter
    {
        public Task<FormatResult> FormatAsync(FormatRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("NoFormat must not call the formatter.");
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
