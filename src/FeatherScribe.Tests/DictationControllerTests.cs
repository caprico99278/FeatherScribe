using FeatherScribe.App;
using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public class DictationControllerTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    private sealed class FakeRecorder : IAudioRecorder
    {
        public Task<RecordedAudio> RecordUntilStoppedAsync(CancellationToken cancellationToken)
        {
            var path = Path.Combine(Path.GetTempPath(), $"fs_controller_test_{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, [0x00]);
            return Task.FromResult(new RecordedAudio(
                new AudioFile(path, TimeSpan.FromSeconds(1)),
                DateTimeOffset.Now,
                DateTimeOffset.Now));
        }
    }

    private sealed class QueueSpeechToText(params string[] texts) : ISpeechToTextEngine
    {
        private readonly Queue<string> _texts = new(texts);

        public Task<TranscriptionResult> TranscribeAsync(AudioFile audioFile, CancellationToken cancellationToken)
        {
            var text = _texts.Dequeue();
            return Task.FromResult(new TranscriptionResult(text, TimeSpan.Zero, true, null));
        }
    }

    private sealed class GatedSpeechToText(string text) : ISpeechToTextEngine
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async Task<TranscriptionResult> TranscribeAsync(
            AudioFile audioFile,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new TranscriptionResult(text, TimeSpan.Zero, true, null);
        }
    }

    private sealed class DelayedFirstFormatter(
        Task<FormatResult> firstResult,
        TaskCompletionSource firstFormattingStarted) : ITextFormatter
    {
        private int _callCount;

        public Task<FormatResult> FormatAsync(FormatRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                firstFormattingStarted.TrySetResult();
                return firstResult;
            }

            return Task.FromResult(new FormatResult($"formatted: {request.RawText}", false, null));
        }
    }

    private sealed class CapturingOutput : ITextOutput
    {
        public Task OutputAsync(string text, OutputMode mode, CancellationToken cancellationToken)
            => Task.CompletedTask;
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

    [Fact]
    public async Task StaleBackgroundFormatting_DoesNotOverwriteLatestResult()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var firstFormatResult = new TaskCompletionSource<FormatResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFormattingStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new DictationPipeline(
            new FakeRecorder(),
            new QueueSpeechToText("first raw", "second raw"),
            new DelayedFirstFormatter(firstFormatResult.Task, firstFormattingStarted),
            new DictionaryCorrector([]),
            new CapturingOutput(),
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);
        var controller = new DictationController(pipeline, settings);

        var firstCompleted = ListenCompleted(controller);
        controller.Toggle(FormattingMode.PlainFast);
        var first = await firstCompleted.WaitAsync(EventTimeout);
        await firstFormattingStarted.Task.WaitAsync(EventTimeout);

        Assert.True(first.BackgroundFormattingStarted);
        Assert.Equal("first raw", controller.LastResult);

        var secondCompleted = ListenCompleted(controller);
        controller.Toggle(FormattingMode.NoFormat);
        var second = await secondCompleted.WaitAsync(EventTimeout);

        Assert.True(second.Success);
        Assert.NotEqual(first.OperationId, second.OperationId);
        Assert.Equal("second raw", controller.LastResult);

        var staleBackground = ListenPipelineBackground(pipeline);
        firstFormatResult.SetResult(new FormatResult("first formatted", false, null));
        var background = await staleBackground.WaitAsync(EventTimeout);

        Assert.Equal(first.OperationId, background.OperationId);
        Assert.Equal("first formatted", background.FormattedText);
        Assert.Equal("second raw", controller.LastResult);
        Assert.Null(controller.LastFormattedResult);
    }

    [Fact]
    public async Task NewOperation_ClearsPreviousLastFormattedResult()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var pipeline = new DictationPipeline(
            new FakeRecorder(),
            new QueueSpeechToText("first raw", "second raw"),
            new DelayedFirstFormatter(
                Task.FromResult(new FormatResult("first formatted", false, null)),
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)),
            new DictionaryCorrector([]),
            new CapturingOutput(),
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);
        var controller = new DictationController(pipeline, settings);

        var firstCompleted = ListenCompleted(controller);
        var firstBackground = ListenControllerBackground(controller);
        controller.Toggle(FormattingMode.PlainFast);

        await firstCompleted.WaitAsync(EventTimeout);
        await firstBackground.WaitAsync(EventTimeout);
        Assert.Equal("first formatted", controller.LastFormattedResult);

        var secondCompleted = ListenCompleted(controller);
        controller.Toggle(FormattingMode.NoFormat);

        Assert.Null(controller.LastFormattedResult);

        var second = await secondCompleted.WaitAsync(EventTimeout);
        Assert.True(second.Success);
        Assert.Equal("second raw", controller.LastResult);
        Assert.Null(controller.LastFormattedResult);
    }

    [Fact]
    public async Task RawFirstCompletion_DoesNotClearFormattedResultSetByBackgroundCompletion()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var speechToText = new GatedSpeechToText("raw text");
        var backgroundFormatResult = new TaskCompletionSource<FormatResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new DictationPipeline(
            new FakeRecorder(),
            speechToText,
            new DelayedFirstFormatter(
                backgroundFormatResult.Task,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)),
            new DictionaryCorrector([]),
            new CapturingOutput(),
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);
        var controller = new DictationController(pipeline, settings);

        var completed = ListenCompleted(controller);
        controller.Toggle(FormattingMode.PlainFast);
        await speechToText.Started.WaitAsync(EventTimeout);

        var operationId = GetLatestOperationId(controller);
        PublishBackgroundCompletion(controller, new BackgroundFormattingResult(
            operationId, FormattingMode.PlainFast, "formatted first", null));
        Assert.Equal("formatted first", controller.LastFormattedResult);

        speechToText.Release();
        var result = await completed.WaitAsync(EventTimeout);

        Assert.True(result.BackgroundFormattingStarted);
        Assert.Equal(operationId, result.OperationId);
        Assert.Equal("formatted first", controller.LastFormattedResult);

        backgroundFormatResult.SetResult(new FormatResult("formatted later", false, null));
    }

    [Fact]
    public async Task ReformatLast_FormatsLastRawResultAgain()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var pipeline = new DictationPipeline(
            new FakeRecorder(),
            new QueueSpeechToText("raw request"),
            new DelayedFirstFormatter(
                Task.FromResult(new FormatResult("raw request", true, "整形結果を破棄しました: request_phrase_removed")),
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)),
            new DictionaryCorrector([]),
            new CapturingOutput(),
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);
        var controller = new DictationController(pipeline, settings);

        var completed = ListenCompleted(controller);
        var firstBackground = ListenControllerBackground(controller);
        controller.Toggle(FormattingMode.PlainFast);

        await completed.WaitAsync(EventTimeout);
        var failedBackground = await firstBackground.WaitAsync(EventTimeout);

        Assert.Null(failedBackground.FormattedText);
        Assert.Equal("raw request", controller.LastRawResult);
        Assert.Equal("raw request", controller.LastResult);

        var secondBackground = ListenControllerBackground(controller);
        Assert.Equal(FormattingMode.PlainQuality, controller.ReformatLast());

        var reformatted = await secondBackground.WaitAsync(EventTimeout);
        Assert.Equal(FormattingMode.PlainQuality, reformatted.Mode);
        Assert.Equal("formatted: raw request", reformatted.FormattedText);
        Assert.Equal("formatted: raw request", controller.LastFormattedResult);
        Assert.Equal("formatted: raw request", controller.LastResult);
        Assert.Equal("raw request", controller.LastRawResult);
    }

    [Fact]
    public async Task RejectedBackgroundCandidate_CanBeAdoptedManually()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var pipeline = new DictationPipeline(
            new FakeRecorder(),
            new QueueSpeechToText("raw request"),
            new DelayedFirstFormatter(
                Task.FromResult(new FormatResult(
                    "raw request",
                    true,
                    "整形結果を破棄しました: request_phrase_removed",
                    "candidate text")),
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)),
            new DictionaryCorrector([]),
            new CapturingOutput(),
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);
        var controller = new DictationController(pipeline, settings);

        var completed = ListenCompleted(controller);
        var background = ListenControllerBackground(controller);
        controller.Toggle(FormattingMode.PlainFast);

        await completed.WaitAsync(EventTimeout);
        await background.WaitAsync(EventTimeout);

        Assert.Equal("candidate text", controller.LastRejectedFormattedResult);
        Assert.Equal("raw request", controller.LastResult);
        Assert.True(controller.HasRejectedCandidate);
        Assert.Equal(new ActionAvailability(true, true, true, true), ActionAvailability.From(controller));

        Assert.True(controller.AdoptRejectedFormattedResult());
        Assert.Equal("candidate text", controller.LastResult);
        Assert.Equal("candidate text", controller.LastFormattedResult);
        Assert.Null(controller.LastRejectedFormattedResult);
        Assert.False(controller.HasRejectedCandidate);
        Assert.Equal(new ActionAvailability(true, true, true, false), ActionAvailability.From(controller));
    }

    [Fact]
    public void CanReformat_FalseWithoutResult()
    {
        var controller = CreateController(LlmEnabled(), new QueueSpeechToText());

        Assert.False(controller.CanReformat);
        Assert.False(controller.HasLatestResult);
        Assert.False(controller.HasRejectedCandidate);
        Assert.Equal(new ActionAvailability(false, false, false, false), ActionAvailability.From(controller));
        AssertReformatLastMatchesCanReformat(controller, expected: false);
    }

    [Fact]
    public async Task CanReformat_FalseAfterNoFormatResult()
    {
        var controller = CreateController(LlmEnabled(), new QueueSpeechToText("raw"));

        await RunOnceAsync(controller, FormattingMode.NoFormat);

        Assert.True(controller.HasLatestResult);
        Assert.Equal(new ActionAvailability(true, true, false, false), ActionAvailability.From(controller));
        AssertReformatLastMatchesCanReformat(controller, expected: false);
    }

    [Fact]
    public async Task CanReformat_FalseWhenLlmDisabled()
    {
        var settings = new AppSettings { Llm = new LlmSettings { Enabled = false, RawFirstPaste = true } };
        var controller = CreateController(settings, new QueueSpeechToText("raw"));

        await RunOnceAsync(controller, FormattingMode.PlainFast);

        Assert.True(controller.HasLatestResult);
        Assert.Equal(new ActionAvailability(true, true, false, false), ActionAvailability.From(controller));
        AssertReformatLastMatchesCanReformat(controller, expected: false);
    }

    [Fact]
    public async Task CanReformat_FalseWhileNotIdle_TrueAfterCompletion()
    {
        var speechToText = new SecondCallGatedSpeechToText("first raw", "second raw");
        var controller = CreateController(LlmEnabled(), speechToText);

        await RunOnceAsync(controller, FormattingMode.PlainFast);
        Assert.True(controller.CanReformat);

        var secondCompleted = ListenCompleted(controller);
        controller.Toggle(FormattingMode.PlainFast);
        await speechToText.SecondStarted.WaitAsync(EventTimeout);

        // Recording/processing: a previous raw result exists, but reformat must not start.
        AssertReformatLastMatchesCanReformat(controller, expected: false);

        speechToText.ReleaseSecond();
        await secondCompleted.WaitAsync(EventTimeout);

        Assert.Equal("second raw", controller.LastRawResult);
        AssertReformatLastMatchesCanReformat(controller, expected: true);
    }

    [Fact]
    public async Task CanReformat_TrueAfterFormattableResult()
    {
        var controller = CreateController(LlmEnabled(), new QueueSpeechToText("raw"));

        await RunOnceAsync(controller, FormattingMode.PlainFast);

        Assert.Equal(new ActionAvailability(true, true, true, false), ActionAvailability.From(controller));
        AssertReformatLastMatchesCanReformat(controller, expected: true);
    }

    [Theory]
    [InlineData(FormattingMode.NoFormat, true, false)]
    [InlineData(FormattingMode.PlainFast, true, true)]
    [InlineData(FormattingMode.PlainQuality, true, true)]
    [InlineData(FormattingMode.Polite, true, true)]
    [InlineData(FormattingMode.Memo, true, true)]
    [InlineData(FormattingMode.PlainFast, false, false)]
    [InlineData(FormattingMode.Bullet, false, false)]
    public async Task ReformatLast_ReturnsNullExactlyWhenCanReformatIsFalse(
        FormattingMode mode,
        bool llmEnabled,
        bool expected)
    {
        var settings = new AppSettings { Llm = new LlmSettings { Enabled = llmEnabled, RawFirstPaste = true } };
        var controller = CreateController(settings, new QueueSpeechToText("raw"));

        await RunOnceAsync(controller, mode);

        AssertReformatLastMatchesCanReformat(controller, expected);
    }

    private static AppSettings LlmEnabled()
        => new() { Llm = new LlmSettings { Enabled = true, RawFirstPaste = true } };

    private static DictationController CreateController(AppSettings settings, ISpeechToTextEngine speechToText)
    {
        var pipeline = new DictationPipeline(
            new FakeRecorder(),
            speechToText,
            new DelayedFirstFormatter(
                Task.FromResult(new FormatResult("formatted", false, null)),
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)),
            new DictionaryCorrector([]),
            new CapturingOutput(),
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);
        return new DictationController(pipeline, settings);
    }

    private static async Task RunOnceAsync(DictationController controller, FormattingMode mode)
    {
        var completed = ListenCompleted(controller);
        controller.Toggle(mode);
        var result = await completed.WaitAsync(EventTimeout);
        Assert.True(result.Success);
    }

    private static void AssertReformatLastMatchesCanReformat(DictationController controller, bool expected)
    {
        var canReformat = controller.CanReformat;
        Assert.Equal(expected, canReformat);
        Assert.Equal(canReformat, controller.ReformatLast() is not null);
    }

    private sealed class SecondCallGatedSpeechToText(string first, string second) : ISpeechToTextEngine
    {
        private readonly TaskCompletionSource _secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseSecond = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public Task SecondStarted => _secondStarted.Task;

        public void ReleaseSecond() => _releaseSecond.TrySetResult();

        public async Task<TranscriptionResult> TranscribeAsync(AudioFile audioFile, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                return new TranscriptionResult(first, TimeSpan.Zero, true, null);
            }

            _secondStarted.TrySetResult();
            await _releaseSecond.Task.WaitAsync(cancellationToken);
            return new TranscriptionResult(second, TimeSpan.Zero, true, null);
        }
    }

    private static Task<PipelineResult> ListenCompleted(DictationController controller)
    {
        var tcs = new TaskCompletionSource<PipelineResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(PipelineResult result)
        {
            controller.Completed -= Handler;
            tcs.TrySetResult(result);
        }

        controller.Completed += Handler;
        return tcs.Task;
    }

    private static Task<BackgroundFormattingResult> ListenPipelineBackground(DictationPipeline pipeline)
    {
        var tcs = new TaskCompletionSource<BackgroundFormattingResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(BackgroundFormattingResult result)
        {
            pipeline.BackgroundFormattingCompleted -= Handler;
            tcs.TrySetResult(result);
        }

        pipeline.BackgroundFormattingCompleted += Handler;
        return tcs.Task;
    }

    private static Task<BackgroundFormattingResult> ListenControllerBackground(DictationController controller)
    {
        var tcs = new TaskCompletionSource<BackgroundFormattingResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(BackgroundFormattingResult result)
        {
            controller.BackgroundFormattingCompleted -= Handler;
            tcs.TrySetResult(result);
        }

        controller.BackgroundFormattingCompleted += Handler;
        return tcs.Task;
    }

    private static Guid GetLatestOperationId(DictationController controller)
    {
        var field = typeof(DictationController).GetField(
            "_latestOperationId",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (Guid)field.GetValue(controller)!;
    }

    private static void PublishBackgroundCompletion(
        DictationController controller,
        BackgroundFormattingResult result)
    {
        var method = typeof(DictationController).GetMethod(
            "OnBackgroundFormattingCompleted",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(controller, [result]);
    }
}
