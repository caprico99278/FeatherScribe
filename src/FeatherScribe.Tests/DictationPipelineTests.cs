using FeatherScribe.Core;

namespace FeatherScribe.Tests;

public class DictationPipelineTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    private sealed class FakeRecorder : IAudioRecorder
    {
        public Task<RecordedAudio> RecordUntilStoppedAsync(CancellationToken cancellationToken)
        {
            var path = Path.Combine(Path.GetTempPath(), $"fs_test_{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, [0x00]);
            return Task.FromResult(new RecordedAudio(
                new AudioFile(path, TimeSpan.FromSeconds(1)),
                DateTimeOffset.Now,
                DateTimeOffset.Now));
        }
    }

    private sealed class FakeSpeechToText(string text, bool success = true) : ISpeechToTextEngine
    {
        public Task<TranscriptionResult> TranscribeAsync(AudioFile audioFile, CancellationToken cancellationToken)
            => Task.FromResult(new TranscriptionResult(
                text, TimeSpan.Zero, success, success ? null : "asr_error"));
    }

    private sealed class FakeFormatter(Func<FormatRequest, Task<FormatResult>> handler) : ITextFormatter
    {
        public int CallCount { get; private set; }

        public FakeFormatter(Func<FormatRequest, FormatResult> handler)
            : this(r => Task.FromResult(handler(r)))
        {
        }

        public Task<FormatResult> FormatAsync(FormatRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return handler(request);
        }
    }

    /// <summary>開始を通知しつつ、キャンセルされるまで完了しないフォーマッタ。</summary>
    private sealed class HangingFormatter(TaskCompletionSource started) : ITextFormatter
    {
        public async Task<FormatResult> FormatAsync(FormatRequest request, CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new FormatResult("unreachable", false, null);
        }
    }

    private sealed class CapturingOutput : ITextOutput
    {
        public List<string> Outputs { get; } = [];

        public Task OutputAsync(string text, OutputMode mode, CancellationToken cancellationToken)
        {
            Outputs.Add(text);
            return Task.CompletedTask;
        }
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

    private static (DictationPipeline Pipeline, FakeFormatter Formatter, CapturingOutput Output) CreatePipeline(
        AppSettings settings,
        FakeFormatter? formatter = null,
        bool transcriptionSucceeds = true,
        string rawText = "えーとこれはテストです")
    {
        formatter ??= new FakeFormatter(r => new FormatResult("整形済み: " + r.RawText, false, null));
        var output = new CapturingOutput();
        var pipeline = new DictationPipeline(
            new FakeRecorder(),
            new FakeSpeechToText(rawText, transcriptionSucceeds),
            formatter,
            new DictionaryCorrector([]),
            output,
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);
        return (pipeline, formatter, output);
    }

    private static Task<PipelineResult> RunAsync(DictationPipeline pipeline, FormattingMode mode)
    {
        // 即時に録音停止済みのトークンを渡す
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        return pipeline.RunAsync(mode, stop.Token, CancellationToken.None);
    }

    private static (Task<BackgroundFormattingResult> Task, Action Unsubscribe) ListenBackground(
        DictationPipeline pipeline)
    {
        var tcs = new TaskCompletionSource<BackgroundFormattingResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(BackgroundFormattingResult r) => tcs.TrySetResult(r);
        pipeline.BackgroundFormattingCompleted += Handler;
        return (tcs.Task, () => pipeline.BackgroundFormattingCompleted -= Handler);
    }

    [Fact]
    public async Task NoFormat_PastesRawWithoutCallingFormatter()
    {
        var settings = new AppSettings { Llm = new LlmSettings { Enabled = true } };
        var (pipeline, formatter, output) = CreatePipeline(settings);

        var result = await RunAsync(pipeline, FormattingMode.NoFormat);

        Assert.True(result.Success);
        Assert.Equal(0, formatter.CallCount);
        Assert.Equal(["えーとこれはテストです"], output.Outputs);
    }

    [Fact]
    public async Task LlmDisabled_FormattingModePastesRawWithoutCallingFormatter()
    {
        var settings = new AppSettings { Llm = new LlmSettings { Enabled = false } };
        var (pipeline, formatter, output) = CreatePipeline(settings);

        var result = await RunAsync(pipeline, FormattingMode.PlainFast);

        Assert.True(result.Success);
        Assert.Equal(0, formatter.CallCount);
        Assert.Single(output.Outputs);
        Assert.False(result.UsedFallback);
        Assert.False(result.BackgroundFormattingStarted);
    }

    [Fact]
    public async Task RawFirst_DelayedFormatter_PipelineCompletesImmediately()
    {
        // formatterが完了しなくても、raw出力後にRunAsyncが即完了すること (指示書002 §1)
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var formatterGate = new TaskCompletionSource<FormatResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var (pipeline, formatter, output) = CreatePipeline(
            settings, new FakeFormatter(_ => formatterGate.Task));

        var runTask = RunAsync(pipeline, FormattingMode.PlainFast);
        var completed = await Task.WhenAny(runTask, Task.Delay(EventTimeout));

        Assert.Same(runTask, completed); // 整形完了を待たずにパイプラインが返る
        var result = await runTask;
        Assert.True(result.Success);
        Assert.True(result.BackgroundFormattingStarted);
        Assert.Equal(["えーとこれはテストです"], output.Outputs);
        Assert.Equal(1, formatter.CallCount); // 整形はバックグラウンドで開始済み

        formatterGate.SetResult(new FormatResult("整形済み", false, null));
    }

    [Fact]
    public async Task RawFirst_FormatterFails_RawOutputIsStillSuccess()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var (pipeline, _, output) = CreatePipeline(
            settings,
            new FakeFormatter(r => new FormatResult(r.RawText, UsedFallback: true, "LLM がタイムアウトしました (8秒)")));
        var (backgroundTask, unsubscribe) = ListenBackground(pipeline);

        var result = await RunAsync(pipeline, FormattingMode.PlainFast);

        Assert.True(result.Success); // raw出力は成功扱い
        Assert.Single(output.Outputs);

        var background = await backgroundTask.WaitAsync(EventTimeout);
        unsubscribe();
        Assert.Null(background.FormattedText);
        Assert.Contains("タイムアウト", background.ErrorMessage);
    }

    [Fact]
    public async Task RawFirst_BackgroundCompletion_RetainsLastFormattedResult()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var (pipeline, _, output) = CreatePipeline(settings);
        var (backgroundTask, unsubscribe) = ListenBackground(pipeline);

        var result = await RunAsync(pipeline, FormattingMode.PlainFast);
        var background = await backgroundTask.WaitAsync(EventTimeout);
        unsubscribe();

        // 貼り付けられたのはrawのみ (自動置換禁止)。整形結果は保持のみ。
        Assert.Equal(["えーとこれはテストです"], output.Outputs);
        Assert.Equal("整形済み: えーとこれはテストです", background.FormattedText);
        Assert.Equal("整形済み: えーとこれはテストです", pipeline.LastBackgroundFormattedText);
        Assert.True(result.BackgroundFormattingStarted);
    }

    [Fact]
    public async Task RawFirst_BackgroundFormatting_DoesNotBlockNextRun()
    {
        // 1回目の整形が終わらない状態でも、2回目の録音〜貼り付けが完了すること
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var formatterGate = new TaskCompletionSource<FormatResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var (pipeline, _, output) = CreatePipeline(
            settings, new FakeFormatter(_ => formatterGate.Task));

        var first = await RunAsync(pipeline, FormattingMode.PlainFast);
        Assert.True(first.BackgroundFormattingStarted);

        // 整形が保留中のまま2回目を実行
        var second = await RunAsync(pipeline, FormattingMode.NoFormat).WaitAsync(EventTimeout);

        Assert.True(second.Success);
        Assert.Equal(2, output.Outputs.Count);

        formatterGate.SetResult(new FormatResult("整形済み", false, null));
    }

    [Fact]
    public async Task TransformationMode_FormatsBeforePasting()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var (pipeline, _, output) = CreatePipeline(settings);

        var result = await RunAsync(pipeline, FormattingMode.Polite);

        // Polite はrawFirst対象外: 整形後テキストのみが貼り付けられる
        Assert.Equal(["整形済み: えーとこれはテストです"], output.Outputs);
        Assert.False(result.BackgroundFormattingStarted);
    }

    [Fact]
    public async Task FormatterFallback_FallbackToRawTrue_PastesRaw()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, FallbackToRaw = true },
        };
        var (pipeline, _, output) = CreatePipeline(
            settings,
            new FakeFormatter(r => new FormatResult(r.RawText, UsedFallback: true, "接続失敗")));

        var result = await RunAsync(pipeline, FormattingMode.Polite);

        Assert.True(result.Success);
        Assert.True(result.UsedFallback);
        Assert.Equal(["えーとこれはテストです"], output.Outputs);
    }

    [Fact]
    public async Task FormatterFallback_FallbackToRawFalse_DoesNotPaste()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, FallbackToRaw = false },
        };
        var (pipeline, _, output) = CreatePipeline(
            settings,
            new FakeFormatter(r => new FormatResult(r.RawText, UsedFallback: true, "接続失敗")));

        var result = await RunAsync(pipeline, FormattingMode.Polite);

        Assert.False(result.Success);
        Assert.Empty(output.Outputs);
        // raw は再コピー用に保持される
        Assert.Equal("えーとこれはテストです", result.Text);
    }

    [Fact]
    public async Task TranscriptionFails_NothingIsOutput()
    {
        var settings = new AppSettings();
        var (pipeline, formatter, output) = CreatePipeline(settings, transcriptionSucceeds: false);

        var result = await RunAsync(pipeline, FormattingMode.NoFormat);

        Assert.False(result.Success);
        Assert.Empty(output.Outputs);
        Assert.Equal(0, formatter.CallCount);
        Assert.Contains("文字起こしに失敗", result.ErrorMessage);
    }

    [Fact]
    public async Task Dispose_CancelsPendingBackgroundFormatting()
    {
        var settings = new AppSettings
        {
            Llm = new LlmSettings { Enabled = true, RawFirstPaste = true },
        };
        var formatterStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new CapturingOutput();
        var pipeline = new DictationPipeline(
            new FakeRecorder(),
            new FakeSpeechToText("えーとこれはテストです"),
            new HangingFormatter(formatterStarted),
            new DictionaryCorrector([]),
            output,
            new EmptyDictionaryProvider(),
            new NullEventLog(),
            settings);
        var (backgroundTask, _) = ListenBackground(pipeline);

        await RunAsync(pipeline, FormattingMode.PlainFast);
        await formatterStarted.Task.WaitAsync(EventTimeout);

        pipeline.Dispose(); // アプリ終了相当

        // キャンセルは静かに処理され、完了イベントは発火しない
        var winner = await Task.WhenAny(backgroundTask, Task.Delay(500));
        Assert.NotSame(backgroundTask, winner);
    }
}
