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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
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
