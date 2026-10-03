using System.Collections.Concurrent;
using FeatherScribe.App;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

/// <summary>
/// The recording limit (recording.maxRecordingSeconds) stops the recording; the user is told once, when
/// processing starts, so speech after the limit is not lost silently. A normal stop shows nothing extra.
/// </summary>
public sealed class RecordingLimitTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    private sealed class FlagRecorder(bool stoppedAtMaxDuration, TimeSpan duration) : IAudioRecorder
    {
        public Task<RecordedAudio> RecordUntilStoppedAsync(CancellationToken cancellationToken)
        {
            var path = Path.Combine(Path.GetTempPath(), $"fs_limit_test_{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, [0x00]);
            return Task.FromResult(new RecordedAudio(
                new AudioFile(path, duration),
                DateTimeOffset.Now,
                DateTimeOffset.Now,
                stoppedAtMaxDuration));
        }
    }

    /// <summary>
    /// First recording: stopped by the limit at once. Later recordings: run until the user's stop (the
    /// hotkey), a normal stop.
    /// </summary>
    private sealed class LimitThenUserStopRecorder : IAudioRecorder
    {
        private int _calls;
        private readonly TaskCompletionSource _laterRecordingStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public Task LaterRecordingStarted => _laterRecordingStarted.Task;

        public async Task<RecordedAudio> RecordUntilStoppedAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            var path = Path.Combine(Path.GetTempPath(), $"fs_limit_test_{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, [0x00]);
            if (call == 1)
            {
                return new RecordedAudio(
                    new AudioFile(path, TimeSpan.FromSeconds(300)), DateTimeOffset.Now, DateTimeOffset.Now, true);
            }

            _laterRecordingStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The user's stop.
            }

            return new RecordedAudio(
                new AudioFile(path, TimeSpan.FromSeconds(2)), DateTimeOffset.Now, DateTimeOffset.Now);
        }
    }

    /// <summary>Holds the first transcription until released; later ones return at once.</summary>
    private sealed class GatedFirstSpeechToText : ISpeechToTextEngine
    {
        private int _calls;
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async Task<TranscriptionResult> TranscribeAsync(AudioFile audioFile, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _started.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return new TranscriptionResult("テストです", TimeSpan.Zero, true, null);
        }
    }

    private sealed class FixedSpeechToText(string text, bool success = true) : ISpeechToTextEngine
    {
        public Task<TranscriptionResult> TranscribeAsync(AudioFile audioFile, CancellationToken cancellationToken)
            => Task.FromResult(new TranscriptionResult(text, TimeSpan.Zero, success, success ? null : "asr_error"));
    }

    private sealed class UnusedFormatter : ITextFormatter
    {
        public Task<FormatResult> FormatAsync(FormatRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new FormatResult(request.RawText, false, null));
    }

    private sealed class NullOutput : ITextOutput
    {
        public Task OutputAsync(string text, OutputMode mode, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class EmptyDictionaryProvider : IDictionaryProvider
    {
        public IReadOnlyList<DictionaryEntry> Load() => [];
    }

    private sealed class CapturingEventLog : IEventLog
    {
        public ConcurrentQueue<PipelineEvent> Events { get; } = new();

        public void Write(PipelineEvent entry) => Events.Enqueue(entry);
    }

    private static DictationPipeline CreatePipeline(
        bool stoppedAtMaxDuration,
        AppSettings? settings = null,
        CapturingEventLog? eventLog = null,
        bool transcriptionSucceeds = true,
        TimeSpan? duration = null)
        => new(
            new FlagRecorder(stoppedAtMaxDuration, duration ?? TimeSpan.FromSeconds(300)),
            new FixedSpeechToText("テストです", transcriptionSucceeds),
            new UnusedFormatter(),
            new DictionaryCorrector([]),
            new NullOutput(),
            new EmptyDictionaryProvider(),
            eventLog ?? new CapturingEventLog(),
            settings ?? new AppSettings());

    private static async Task<PipelineResult> RunStoppedAsync(DictationPipeline pipeline, Guid operationId)
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        return await pipeline.RunAsync(FormattingMode.NoFormat, stop.Token, CancellationToken.None, operationId);
    }

    // --- Recorder decision ---

    [Theory]
    [InlineData(false, true, true)]   // the limit fired first
    [InlineData(true, false, false)]  // the user's hotkey
    [InlineData(true, true, false)]   // both: the user's stop wins, no notice
    [InlineData(false, false, false)] // neither (e.g. the device stopped)
    public void Recorder_ReportsLimitStopOnlyWhenTheLimitStoppedIt(
        bool userStopRequested, bool maxDurationElapsed, bool expected)
    {
        Assert.Equal(expected, NAudioRecorder.IsMaxDurationStop(userStopRequested, maxDurationElapsed));
    }

    [Fact]
    public void RecordedAudio_DefaultsToNormalStop()
    {
        var audio = new RecordedAudio(new AudioFile("a.wav", TimeSpan.FromSeconds(1)), DateTimeOffset.Now, DateTimeOffset.Now);

        Assert.False(audio.StoppedAtMaxDuration);
    }

    // --- Pipeline ---

    [Fact]
    public async Task Pipeline_LimitStop_RaisesNoticeAfterTranscribingStartsAndLogsMetadata()
    {
        var eventLog = new CapturingEventLog();
        var settings = new AppSettings { Recording = new RecordingSettings { MaxRecordingSeconds = 90 } };
        var pipeline = CreatePipeline(true, settings, eventLog, duration: TimeSpan.FromMilliseconds(90_050));
        var order = new List<string>();
        var notices = new List<RecordingLimitNotice>();
        pipeline.StageChanged += (stage, _) => order.Add(stage.ToString());
        pipeline.RecordingLimitReached += notice =>
        {
            order.Add("limit");
            notices.Add(notice);
        };
        var operationId = Guid.NewGuid();

        var result = await RunStoppedAsync(pipeline, operationId);

        Assert.True(result.Success);
        var notice = Assert.Single(notices);
        Assert.Equal(operationId, notice.OperationId);
        Assert.Equal(90, notice.LimitSeconds);
        Assert.Equal(["Recording", "Transcribing", "limit", "Completed"], order);

        var limitEvent = Assert.Single(eventLog.Events, e => e.Stage == "recording_max_duration");
        Assert.True(limitEvent.Success);
        Assert.Null(limitEvent.ErrorType);
        Assert.Equal(90_050, limitEvent.DurationMilliseconds);
        Assert.Equal(0, limitEvent.CharCount);
    }

    [Fact]
    public async Task Pipeline_LimitStop_StillNotifiesWhenTranscriptionFails()
    {
        var pipeline = CreatePipeline(true, transcriptionSucceeds: false);
        var notices = 0;
        pipeline.RecordingLimitReached += _ => notices++;

        var result = await RunStoppedAsync(pipeline, Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal(1, notices);
    }

    [Fact]
    public async Task Pipeline_FailingNoticeSubscriber_DoesNotLoseTheDictation()
    {
        var pipeline = CreatePipeline(true);
        pipeline.RecordingLimitReached += _ => throw new InvalidOperationException("ui unavailable");

        var result = await RunStoppedAsync(pipeline, Guid.NewGuid());

        Assert.True(result.Success);
        Assert.Equal("テストです", result.Text);
    }

    [Fact]
    public async Task Pipeline_NormalStop_RaisesNothingExtra()
    {
        var eventLog = new CapturingEventLog();
        var pipeline = CreatePipeline(false, eventLog: eventLog);
        var notices = 0;
        pipeline.RecordingLimitReached += _ => notices++;

        var result = await RunStoppedAsync(pipeline, Guid.NewGuid());

        Assert.True(result.Success);
        Assert.Equal(0, notices);
        Assert.DoesNotContain(eventLog.Events, e => e.Stage == "recording_max_duration");
    }

    [Fact]
    public async Task Pipeline_LimitBelowOne_IsReportedAsTheRecorderMinimum()
    {
        var settings = new AppSettings { Recording = new RecordingSettings { MaxRecordingSeconds = 0 } };
        var pipeline = CreatePipeline(true, settings);
        RecordingLimitNotice? notice = null;
        pipeline.RecordingLimitReached += n => notice = n;

        await RunStoppedAsync(pipeline, Guid.NewGuid());

        Assert.Equal(1, notice?.LimitSeconds);
    }

    // --- Controller ---

    [Fact]
    public async Task Controller_ForwardsLimitNoticeOfLatestOperationOnce()
    {
        var settings = new AppSettings();
        var pipeline = CreatePipeline(true, settings);
        var controller = new DictationController(pipeline, settings);
        var notices = new ConcurrentQueue<RecordingLimitNotice>();
        controller.RecordingLimitReached += notices.Enqueue;
        var completed = ListenCompleted(controller);

        controller.Toggle(FormattingMode.NoFormat);
        var result = await completed.WaitAsync(EventTimeout);

        var notice = Assert.Single(notices);
        Assert.Equal(result.OperationId, notice.OperationId);
        Assert.Equal(300, notice.LimitSeconds);
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public async Task Controller_NormalStop_ForwardsNothing()
    {
        var settings = new AppSettings();
        var pipeline = CreatePipeline(false, settings);
        var controller = new DictationController(pipeline, settings);
        var notices = 0;
        controller.RecordingLimitReached += _ => Interlocked.Increment(ref notices);
        var completed = ListenCompleted(controller);

        controller.Toggle(FormattingMode.NoFormat);
        await completed.WaitAsync(EventTimeout);

        Assert.Equal(0, notices);
    }

    [Fact]
    public async Task Controller_IgnoresLimitNoticeOfStaleOperation()
    {
        var settings = new AppSettings();
        var pipeline = CreatePipeline(true, settings);
        var controller = new DictationController(pipeline, settings);
        var notices = 0;
        controller.RecordingLimitReached += _ => Interlocked.Increment(ref notices);
        var pipelineNotices = 0;
        pipeline.RecordingLimitReached += _ => Interlocked.Increment(ref pipelineNotices);

        // A run the controller did not start (not its latest operation id).
        await RunStoppedAsync(pipeline, Guid.NewGuid());

        Assert.Equal(1, pipelineNotices);
        Assert.Equal(0, notices);
        Assert.Equal("Idle", GetState(controller));
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public async Task Controller_AutoStop_MovesToProcessing_IgnoresHotkey_ThenNextHotkeyRecordsAgain()
    {
        var settings = new AppSettings();
        var recorder = new LimitThenUserStopRecorder();
        var speechToText = new GatedFirstSpeechToText();
        var pipeline = new DictationPipeline(
            recorder, speechToText, new UnusedFormatter(), new DictionaryCorrector([]),
            new NullOutput(), new EmptyDictionaryProvider(), new CapturingEventLog(), settings);
        var controller = new DictationController(pipeline, settings);
        var notices = 0;
        controller.RecordingLimitReached += _ => Interlocked.Increment(ref notices);

        // 1. The limit stops the recording: the controller is processing (not still recording).
        var firstCompleted = ListenCompleted(controller);
        controller.Toggle(FormattingMode.NoFormat);
        await speechToText.Started.WaitAsync(EventTimeout);

        Assert.Equal("Processing", GetState(controller));
        Assert.True(controller.IsBusy);
        Assert.Equal(1, notices);
        var firstOperationId = GetLatestOperationId(controller);

        // 2. A hotkey press while transcribing does nothing (no stop, no new recording).
        controller.Toggle(FormattingMode.NoFormat);

        Assert.Equal("Processing", GetState(controller));
        Assert.Equal(1, recorder.Calls);
        Assert.Equal(firstOperationId, GetLatestOperationId(controller));

        // 3. After completion the controller is idle.
        speechToText.Release();
        var first = await firstCompleted.WaitAsync(EventTimeout);

        Assert.True(first.Success);
        Assert.Equal(firstOperationId, first.OperationId);
        Assert.Equal("Idle", GetState(controller));
        Assert.False(controller.IsBusy);

        // 4. The next press starts a new recording; the user's stop is a normal stop (no notice).
        var secondCompleted = ListenCompleted(controller);
        controller.Toggle(FormattingMode.NoFormat);
        await recorder.LaterRecordingStarted.WaitAsync(EventTimeout);

        Assert.Equal("Recording", GetState(controller));
        Assert.Equal(2, recorder.Calls);

        controller.Toggle(FormattingMode.NoFormat);
        var second = await secondCompleted.WaitAsync(EventTimeout);

        Assert.True(second.Success);
        Assert.NotEqual(firstOperationId, second.OperationId);
        Assert.Equal("Idle", GetState(controller));
        Assert.Equal(1, notices);
    }

    [Fact]
    public async Task Controller_UserStop_KeepsNormalStateTransitions()
    {
        var settings = new AppSettings();
        var recorder = new LimitThenUserStopRecorder();
        var pipeline = new DictationPipeline(
            recorder, new FixedSpeechToText("テストです"), new UnusedFormatter(), new DictionaryCorrector([]),
            new NullOutput(), new EmptyDictionaryProvider(), new CapturingEventLog(), settings);
        var controller = new DictationController(pipeline, settings);

        // Use up the recorder's limit-stop recording first.
        var warmUp = ListenCompleted(controller);
        controller.Toggle(FormattingMode.NoFormat);
        await warmUp.WaitAsync(EventTimeout);

        var notices = 0;
        controller.RecordingLimitReached += _ => Interlocked.Increment(ref notices);
        var completed = ListenCompleted(controller);
        controller.Toggle(FormattingMode.NoFormat);
        await recorder.LaterRecordingStarted.WaitAsync(EventTimeout);

        Assert.Equal("Recording", GetState(controller));

        controller.Toggle(FormattingMode.NoFormat);
        var result = await completed.WaitAsync(EventTimeout);

        Assert.True(result.Success);
        Assert.Equal("Idle", GetState(controller));
        Assert.Equal(0, notices);
    }

    // --- UI text and overlay ---

    [Theory]
    [InlineData(300, "5分")]
    [InlineData(60, "1分")]
    [InlineData(120, "2分")]
    [InlineData(90, "90秒")]
    [InlineData(45, "45秒")]
    [InlineData(119, "119秒")]
    [InlineData(150, "2分30秒")]
    [InlineData(121, "2分1秒")]
    [InlineData(0, "1秒")]
    public void LimitLabel_FormatsMinutesAndSeconds(int seconds, string expected)
    {
        Assert.Equal(expected, UserFacingText.RecordingLimitLabel(seconds));
    }

    [Fact]
    public void Overlay_LimitStop_ShowsReasonWhileTranscribing()
    {
        var presentation = OverlayPresentationMapper.FromRecordingLimitReached(new RecordingLimitNotice(Guid.NewGuid(), 300));

        Assert.Equal(OverlayVisualState.Transcribing, presentation.State);
        Assert.True(presentation.IsPersistent);
        Assert.False(presentation.ShowsElapsed);
        Assert.Equal("録音上限（5分）で停止・文字起こし中", presentation.Text);
    }

    [Fact]
    public void Overlay_LimitStop_UsesConfiguredLimit()
    {
        var presentation = OverlayPresentationMapper.FromRecordingLimitReached(new RecordingLimitNotice(Guid.NewGuid(), 90));

        Assert.Equal("録音上限（90秒）で停止・文字起こし中", presentation.Text);
    }

    [Fact]
    public void Overlay_NormalTranscribing_IsUnchanged()
    {
        Assert.Equal("文字起こし中", OverlayPresentationMapper.FromStage(PipelineStage.Transcribing).Text);
    }

    [Fact]
    public void Tray_LimitStop_TellsUserToRecordTheRest()
    {
        var notice = UserFacingText.RecordingLimitTrayNotice(300);

        Assert.Equal("録音上限に達しました", notice.Title);
        Assert.Equal("5分で録音を自動停止しました。続きは、文字起こしが終わってからもう一度ホットキーを押して録音してください。", notice.Body);
        Assert.Equal(
            "2分30秒で録音を自動停止しました。続きは、文字起こしが終わってからもう一度ホットキーを押して録音してください。",
            UserFacingText.RecordingLimitTrayNotice(150).Body);
    }

    [Fact]
    public void App_ShowsLimitNoticeThroughMapperAndUserFacingText()
    {
        var app = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App", "App.xaml.cs"));
        var handler = app[app.IndexOf("controller.RecordingLimitReached +=", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("});", StringComparison.Ordinal)];

        Assert.Contains("Dispatcher.Invoke(", handler);
        Assert.Contains("OverlayPresentationMapper.FromRecordingLimitReached(notice)", handler);
        Assert.Contains("UserFacingText.RecordingLimitTrayNotice(notice.LimitSeconds)", handler);
        Assert.Contains("_trayIconService!.Notify(trayNotice.Title, trayNotice.Body);", handler);
    }

    private static Task<PipelineResult> ListenCompleted(DictationController controller)
    {
        var tcs = new TaskCompletionSource<PipelineResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(PipelineResult result)
        {
            controller.Completed -= Handler;
            tcs.TrySetResult(result);
        }

        controller.Completed += Handler;
        return tcs.Task;
    }

    private static string GetState(DictationController controller)
        => typeof(DictationController)
            .GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(controller)!
            .ToString()!;

    private static Guid GetLatestOperationId(DictationController controller)
        => (Guid)typeof(DictationController)
            .GetField("_latestOperationId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(controller)!;

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

        throw new DirectoryNotFoundException("FeatherScribe.slnx was not found.");
    }
}
