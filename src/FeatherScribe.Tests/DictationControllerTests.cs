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
}
