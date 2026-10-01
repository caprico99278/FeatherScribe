using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using FeatherScribe.App;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

/// <summary>
/// Selected-text editing orchestration (Phase UX-1) with a fake clipboard, a fake keyboard, a fake target app
/// and an immediate fake delay. Never touches the real clipboard and never sends real keystrokes.
/// </summary>
public sealed class SelectionEditServiceTests
{
    private const string Original = "user clipboard";
    private const string Selected = "選択した文章です";
    private const string Edited = "整形後の文章です。";
    private static readonly IntPtr TargetWindow = new(42);
    private static readonly IntPtr OtherWindow = new(77);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>In-memory clipboard with a sequence number, like the Win32 clipboard.</summary>
    private sealed class FakeClipboard : IClipboardAccess
    {
        private sealed record Box(object? Value);

        public object? Content { get; set; } = Original;
        public uint Sequence { get; set; } = 100;
        public bool MarkEmptySelection { get; set; }
        public bool UnsupportedSnapshot { get; set; }
        public List<string> Events { get; } = [];
        public int RestoreCount { get; private set; }

        // Failure injection: which call (1-based count) throws.
        public int SnapshotThrowsOnCall { get; set; }
        public bool ReadThrows { get; set; }
        public bool RestoreThrows { get; set; }
        public int RestoreThrowsOnCall { get; set; }
        public bool SetThrows { get; set; }
        public Action? AfterSet { get; set; }

        private int _snapshotCalls;
        private int _restoreCalls;

        public void Write(object? content)
        {
            Content = content;
            Sequence++;
        }

        public Task<ClipboardSnapshot> CaptureSnapshotAsync()
        {
            Events.Add("snapshot");
            if (++_snapshotCalls == SnapshotThrowsOnCall)
            {
                throw new COMException("clipboard locked");
            }

            if (UnsupportedSnapshot)
            {
                return Task.FromResult(ClipboardSnapshot.Unsupported(ClipboardSnapshot.ReasonTooLarge));
            }

            return Task.FromResult(Content is null
                ? ClipboardSnapshot.Empty
                : ClipboardSnapshot.Captured(new Box(Content), 1));
        }

        public Task SetTextAsync(string text)
        {
            Events.Add("set");
            if (SetThrows)
            {
                throw new COMException("clipboard locked");
            }

            Write(text);
            return Task.CompletedTask;
        }

        public Task<uint> SetTextAndGetSequenceAsync(string text)
        {
            Events.Add("set+seq");
            if (SetThrows)
            {
                throw new ExternalException("clipboard locked");
            }

            Write(text);
            var sequence = Sequence;
            AfterSet?.Invoke();
            return Task.FromResult(sequence);
        }

        public uint GetSequenceNumber() => Sequence;

        public Task<bool> RestoreAsync(ClipboardSnapshot snapshot, uint expectedSequence)
        {
            Events.Add("restore");
            if (RestoreThrows || ++_restoreCalls == RestoreThrowsOnCall)
            {
                throw new COMException("clipboard locked");
            }

            if (Sequence != expectedSequence || snapshot.Kind == SnapshotKind.Unsupported)
            {
                return Task.FromResult(false);
            }

            Write(snapshot.Kind == SnapshotKind.Empty ? null : ((Box)snapshot.Payload!).Value);
            RestoreCount++;
            return Task.FromResult(true);
        }

        public Task<CapturedSelection> ReadSelectionAsync()
        {
            Events.Add("read");
            if (ReadThrows)
            {
                throw new InvalidOperationException("clipboard unavailable");
            }

            return Task.FromResult(new CapturedSelection(Content as string, MarkEmptySelection));
        }
    }

    /// <summary>Fake keyboard + target app: Ctrl+C copies the selection, Ctrl+V pastes the clipboard text.</summary>
    private sealed class FakeKeyboard(FakeClipboard clipboard) : ISelectionKeyboard
    {
        public bool ModifiersReleased { get; set; } = true;
        public string? Selection { get; set; } = Selected;
        public bool PasteThrows { get; set; }
        public int CopyCount { get; private set; }
        public List<string> Pasted { get; } = [];

        public Task<bool> WaitForModifierReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            clipboard.Events.Add("wait-modifiers");
            return Task.FromResult(ModifiersReleased);
        }

        public void SendCopy()
        {
            clipboard.Events.Add("copy");
            CopyCount++;
            if (Selection is not null)
            {
                clipboard.Write(Selection);
            }
        }

        public void SendPaste()
        {
            clipboard.Events.Add("paste");
            if (PasteThrows)
            {
                throw new InvalidOperationException("SendInput failed");
            }

            Pasted.Add(clipboard.Content as string ?? "");
        }
    }

    private sealed class FakeFormatter(Func<FormatRequest, Task<FormatResult>> handler) : ITextFormatter
    {
        public List<FormatRequest> Requests { get; } = [];

        public Task<FormatResult> FormatAsync(FormatRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return handler(request);
        }
    }

    private sealed class FixedDictionaryProvider : IDictionaryProvider
    {
        public static readonly DictionaryEntry Entry = new(["ふぇざー"], "Feather");

        public IReadOnlyList<DictionaryEntry> Load() => [Entry];
    }

    private sealed class RecordingEventLog : IEventLog
    {
        public List<PipelineEvent> Entries { get; } = [];

        public void Write(PipelineEvent entry) => Entries.Add(entry);
    }

    private sealed class Harness
    {
        public Harness()
        {
            Keyboard = new FakeKeyboard(Clipboard);
            Formatter = new FakeFormatter(_ => Task.FromResult(new FormatResult(Edited, false, null)));
        }

        public FakeClipboard Clipboard { get; } = new();
        public FakeKeyboard Keyboard { get; }
        public FakeFormatter Formatter { get; set; }
        public RecordingEventLog EventLog { get; } = new();
        public IntPtr Foreground { get; set; } = TargetWindow;
        public bool OwnForeground { get; set; }
        public bool DictationBusy { get; set; }
        public bool LlmEnabled { get; set; } = true;
        public string Mode { get; set; } = "Bullet";
        public int CaptureTimeoutMilliseconds { get; set; } = 600;
        public List<TimeSpan> Delays { get; } = [];

        public SelectionEditService Create() => new(
            Clipboard,
            Keyboard,
            Formatter,
            new FixedDictionaryProvider(),
            () => Foreground,
            () => OwnForeground,
            () => DictationBusy,
            new AppSettings
            {
                Llm = new LlmSettings { Enabled = LlmEnabled },
                SelectionEdit = new SelectionEditSettings
                {
                    Mode = Mode,
                    CaptureTimeoutMilliseconds = CaptureTimeoutMilliseconds,
                },
            },
            (delay, _) =>
            {
                Delays.Add(delay);
                return Task.CompletedTask;
            },
            EventLog);

        public Task<SelectionEditOutcome> RunAsync() => Create().RunAsync();
    }

    // --- Guards ---

    [Fact]
    public async Task LlmDisabled_DoesNothingAndNotifies()
    {
        var harness = new Harness { LlmEnabled = false };

        var outcome = await harness.RunAsync();

        AssertNothingTouched(harness, outcome, SelectionEditStatus.LlmDisabled);
        Assert.Equal("LLM整形がオフのため、選択テキストの編集は使えません", outcome.Notice);
    }

    [Fact]
    public async Task DictationBusy_DoesNothingAndNotifies()
    {
        var harness = new Harness { DictationBusy = true };

        var outcome = await harness.RunAsync();

        AssertNothingTouched(harness, outcome, SelectionEditStatus.Busy);
        Assert.Equal("処理中のため、選択テキストの編集を開始できません", outcome.Notice);
    }

    [Fact]
    public async Task OwnWindowForeground_DoesNothingAndReportsCaptureFailure()
    {
        var harness = new Harness { OwnForeground = true };

        var outcome = await harness.RunAsync();

        AssertNothingTouched(harness, outcome, SelectionEditStatus.CaptureFailed);
        Assert.Equal("選択テキストを取得できませんでした", outcome.Notice);
    }

    [Fact]
    public async Task ModifierStillHeld_AbortsBeforeCopy()
    {
        var harness = new Harness();
        harness.Keyboard.ModifiersReleased = false;

        var outcome = await harness.RunAsync();

        AssertNothingTouched(harness, outcome, SelectionEditStatus.CaptureFailed);
        Assert.Equal("modifier_held", outcome.Reason);
        Assert.Equal(["wait-modifiers"], harness.Clipboard.Events);
    }

    [Fact]
    public async Task NoForegroundWindow_AbortsBeforeCopy()
    {
        var harness = new Harness { Foreground = IntPtr.Zero };

        var outcome = await harness.RunAsync();

        AssertNothingTouched(harness, outcome, SelectionEditStatus.CaptureFailed);
    }

    // --- Capture ---

    [Fact]
    public async Task CaptureTimeout_AbortsWithoutRestoreOrFormat()
    {
        var harness = new Harness();
        harness.Keyboard.Selection = null; // Ctrl+C with no selection leaves the clipboard unchanged.

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.CaptureFailed, outcome.Status);
        Assert.Equal("capture_timeout", outcome.Reason);
        Assert.Equal("選択テキストを取得できませんでした", outcome.Notice);
        Assert.Equal(1, harness.Keyboard.CopyCount);
        Assert.Empty(harness.Formatter.Requests);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.DoesNotContain("restore", harness.Clipboard.Events);
        Assert.DoesNotContain("read", harness.Clipboard.Events);
        Assert.Equal(Original, harness.Clipboard.Content);
        Assert.Equal(100u, harness.Clipboard.Sequence);
        // 600 ms / 20 ms polls.
        Assert.Equal(30, harness.Delays.Count(delay => delay == SelectionEditService.SequencePollInterval));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n")]
    public async Task EmptyCapture_AbortsAndRestoresClipboard(string selection)
    {
        var harness = new Harness();
        harness.Keyboard.Selection = selection;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.CaptureFailed, outcome.Status);
        Assert.Equal("empty_selection", outcome.Reason);
        Assert.Empty(harness.Formatter.Requests);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.Equal(Original, harness.Clipboard.Content);
        Assert.Equal(1, harness.Clipboard.RestoreCount);
    }

    [Fact]
    public async Task VsCodeEmptySelectionMarker_AbortsWithoutFormatAndRestoresClipboard()
    {
        var harness = new Harness();
        harness.Keyboard.Selection = "whole current line\r\n";
        harness.Clipboard.MarkEmptySelection = true;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.CaptureFailed, outcome.Status);
        Assert.Equal("empty_selection_line_copy", outcome.Reason);
        Assert.Equal("選択テキストを取得できませんでした", outcome.Notice);
        Assert.Empty(harness.Formatter.Requests);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.Equal(Original, harness.Clipboard.Content);
        Assert.Equal(1, harness.Clipboard.RestoreCount);
    }

    [Fact]
    public async Task CopyArrivingLate_WithinTimeout_IsCaptured()
    {
        var harness = new Harness();
        harness.Keyboard.Selection = null;
        var polls = 0;
        var service = new SelectionEditService(
            harness.Clipboard,
            harness.Keyboard,
            harness.Formatter,
            new FixedDictionaryProvider(),
            () => harness.Foreground,
            () => false,
            () => false,
            new AppSettings { Llm = new LlmSettings { Enabled = true } },
            (delay, _) =>
            {
                if (delay == SelectionEditService.SequencePollInterval && ++polls == 5)
                {
                    harness.Clipboard.Write(Selected); // the target app copies ~100 ms later
                }

                return Task.CompletedTask;
            });

        var outcome = await service.RunAsync();

        Assert.Equal(SelectionEditStatus.Replaced, outcome.Status);
        Assert.Equal(5, polls);
        Assert.Equal(Selected, Assert.Single(harness.Formatter.Requests).RawText);
    }

    // --- Success ---

    [Fact]
    public async Task Success_CopiesAfterModifierRelease_FormatsWithMode_PastesOnce_RestoresOriginal()
    {
        var harness = new Harness();

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.Replaced, outcome.Status);
        Assert.True(outcome.Succeeded);
        Assert.Equal("選択範囲を置き換えました", outcome.Notice);
        Assert.Equal(Selected.Length, outcome.CharCount);

        var events = harness.Clipboard.Events;
        Assert.True(events.IndexOf("wait-modifiers") < events.IndexOf("copy"));
        Assert.True(events.IndexOf("snapshot") < events.IndexOf("copy"));
        Assert.True(events.IndexOf("copy") < events.IndexOf("read"));
        Assert.True(events.LastIndexOf("set+seq") < events.IndexOf("paste"));
        Assert.Equal("restore", events[^1]);

        var request = Assert.Single(harness.Formatter.Requests);
        Assert.Equal(Selected, request.RawText);
        Assert.Equal(FormattingMode.Bullet, request.Mode);
        Assert.Equal([FixedDictionaryProvider.Entry], request.DictionaryEntries);

        Assert.Equal([Edited], harness.Keyboard.Pasted);
        Assert.Equal(Original, harness.Clipboard.Content);
        Assert.Equal(2, harness.Clipboard.RestoreCount); // after the capture and after the paste
        Assert.Contains(SelectionEditService.CopySettleDelay, harness.Delays);
        Assert.Equal(SelectionEditService.RestoreDelay, harness.Delays[^1]);
    }

    [Fact]
    public async Task Success_ClipboardChangedAfterPaste_IsNotRestored()
    {
        var harness = new Harness();
        var service = new SelectionEditService(
            harness.Clipboard,
            harness.Keyboard,
            harness.Formatter,
            new FixedDictionaryProvider(),
            () => harness.Foreground,
            () => false,
            () => false,
            new AppSettings { Llm = new LlmSettings { Enabled = true } },
            (delay, _) =>
            {
                if (delay == SelectionEditService.RestoreDelay)
                {
                    harness.Clipboard.Write("copied by the user after the paste");
                }

                return Task.CompletedTask;
            });

        var outcome = await service.RunAsync();

        Assert.Equal(SelectionEditStatus.Replaced, outcome.Status);
        Assert.Equal([Edited], harness.Keyboard.Pasted);
        Assert.Equal("copied by the user after the paste", harness.Clipboard.Content);
        Assert.Equal(1, harness.Clipboard.RestoreCount); // only the restore right after the capture
    }

    [Fact]
    public async Task Success_UserCopiedDuringFormatting_KeepsTheNewerClipboard()
    {
        var harness = new Harness();
        harness.Formatter = new FakeFormatter(_ =>
        {
            harness.Clipboard.Write("copied while waiting");
            return Task.FromResult(new FormatResult(Edited, false, null));
        });

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.Replaced, outcome.Status);
        Assert.Equal([Edited], harness.Keyboard.Pasted);
        Assert.Equal("copied while waiting", harness.Clipboard.Content);
    }

    [Fact]
    public async Task Success_EmptyOriginalClipboard_IsClearedAgain()
    {
        var harness = new Harness();
        harness.Clipboard.Content = null;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.Replaced, outcome.Status);
        Assert.Null(harness.Clipboard.Content);
    }

    [Fact]
    public async Task Success_UnsupportedSnapshot_StillReplacesWithoutRestore()
    {
        var harness = new Harness();
        harness.Clipboard.UnsupportedSnapshot = true;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.Replaced, outcome.Status);
        Assert.Equal([Edited], harness.Keyboard.Pasted);
        Assert.Equal(0, harness.Clipboard.RestoreCount);
        Assert.DoesNotContain(SelectionEditService.RestoreDelay, harness.Delays);
    }

    [Theory]
    [InlineData("NoFormat", FormattingMode.Polite)]
    [InlineData("unknown", FormattingMode.Polite)]
    [InlineData("DevInstruction", FormattingMode.DevInstruction)]
    public async Task Mode_InvalidOrNoFormat_FallsBackToPolite(string configured, FormattingMode expected)
    {
        var harness = new Harness { Mode = configured };

        await harness.RunAsync();

        Assert.Equal(expected, Assert.Single(harness.Formatter.Requests).Mode);
    }

    // --- Edit failures ---

    [Fact]
    public async Task FormatterFallback_DoesNotPasteAndRestoresClipboard()
    {
        var harness = new Harness();
        harness.Formatter = new FakeFormatter(request =>
            Task.FromResult(new FormatResult(request.RawText, true, "整形結果を破棄しました: markdown_structure", "rejected")));

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.EditFailed, outcome.Status);
        Assert.Equal("format_fallback", outcome.Reason);
        Assert.Equal("編集できなかったため、選択テキストは変更していません", outcome.Notice);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.DoesNotContain("set+seq", harness.Clipboard.Events);
        Assert.Equal(Original, harness.Clipboard.Content);
    }

    [Fact]
    public async Task FormatterThrows_DoesNotPasteAndDoesNotThrow()
    {
        var harness = new Harness();
        harness.Formatter = new FakeFormatter(_ => throw new HttpRequestException("down"));

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.EditFailed, outcome.Status);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.Equal(Original, harness.Clipboard.Content);
    }

    [Fact]
    public async Task FormatterReturnsBlank_DoesNotPaste()
    {
        var harness = new Harness();
        harness.Formatter = new FakeFormatter(_ => Task.FromResult(new FormatResult("  ", false, null)));

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.EditFailed, outcome.Status);
        Assert.Empty(harness.Keyboard.Pasted);
    }

    // --- Target changed / stale ---

    [Fact]
    public async Task ForegroundChangedDuringFormatting_DoesNotPaste_PutsEditedTextOnClipboard()
    {
        var harness = new Harness();
        harness.Formatter = new FakeFormatter(_ =>
        {
            harness.Foreground = OtherWindow;
            return Task.FromResult(new FormatResult(Edited, false, null));
        });

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.TargetChanged, outcome.Status);
        Assert.Equal("foreground_changed", outcome.Reason);
        Assert.Equal("入力先が変わったため置き換えませんでした。編集結果はクリップボードにあります", outcome.Notice);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.Equal(Edited, harness.Clipboard.Content);
        Assert.Equal(1, harness.Clipboard.RestoreCount); // after the capture only, never after "set"
        Assert.Equal("set", harness.Clipboard.Events[^1]);
    }

    [Fact]
    public async Task ForegroundChangedRightBeforePaste_DoesNotPaste_KeepsEditedTextOnClipboard()
    {
        var harness = new Harness();
        harness.Clipboard.AfterSet = () => harness.Foreground = OtherWindow;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.TargetChanged, outcome.Status);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.Equal(Edited, harness.Clipboard.Content);
        Assert.Equal(1, harness.Clipboard.RestoreCount);
    }

    [Fact]
    public async Task ForegroundChangedBeforeCopy_AbortsWithoutCopy()
    {
        var harness = new Harness();
        var calls = 0;
        var service = new SelectionEditService(
            harness.Clipboard,
            harness.Keyboard,
            harness.Formatter,
            new FixedDictionaryProvider(),
            () => ++calls == 1 ? TargetWindow : OtherWindow,
            () => false,
            () => false,
            new AppSettings { Llm = new LlmSettings { Enabled = true } },
            (_, _) => Task.CompletedTask);

        var outcome = await service.RunAsync();

        Assert.Equal(SelectionEditStatus.CaptureFailed, outcome.Status);
        Assert.Equal(0, harness.Keyboard.CopyCount);
        Assert.Equal(Original, harness.Clipboard.Content);
    }

    [Fact]
    public async Task DictationStartedDuringFormatting_DoesNotPaste()
    {
        var harness = new Harness();
        harness.Formatter = new FakeFormatter(_ =>
        {
            harness.DictationBusy = true;
            return Task.FromResult(new FormatResult(Edited, false, null));
        });

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.TargetChanged, outcome.Status);
        Assert.Equal("dictation_started", outcome.Reason);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.Equal(Edited, harness.Clipboard.Content);
    }

    [Fact]
    public async Task SecondRequestWhileRunning_IsBusy_AndFirstDoesNotPaste()
    {
        var harness = new Harness();
        var formattingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<FormatResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Formatter = new FakeFormatter(_ =>
        {
            formattingStarted.TrySetResult();
            return release.Task;
        });
        var service = harness.Create();

        var first = service.RunAsync();
        await formattingStarted.Task.WaitAsync(TestTimeout);
        var second = await service.RunAsync().WaitAsync(TestTimeout);
        release.SetResult(new FormatResult(Edited, false, null));
        var firstOutcome = await first.WaitAsync(TestTimeout);

        Assert.Equal(SelectionEditStatus.Busy, second.Status);
        Assert.Equal("処理中のため、選択テキストの編集を開始できません", second.Notice);
        Assert.Equal(SelectionEditStatus.TargetChanged, firstOutcome.Status);
        Assert.Equal("stale_operation", firstOutcome.Reason);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.Equal(1, harness.Keyboard.CopyCount);
        Assert.Equal(Edited, harness.Clipboard.Content);

        // The service is free again afterwards.
        var third = await service.RunAsync().WaitAsync(TestTimeout);
        Assert.NotEqual(SelectionEditStatus.Busy, third.Status);
    }

    [Fact]
    public async Task FormattingStarted_IsRaisedOnlyAfterCapture()
    {
        var harness = new Harness();
        var service = harness.Create();
        var raised = 0;
        service.FormattingStarted += () =>
        {
            Assert.Contains("read", harness.Clipboard.Events);
            raised++;
        };

        await service.RunAsync();

        Assert.Equal(1, raised);
    }

    // --- Replacement failures ---

    [Fact]
    public async Task PasteSenderThrows_RestoresOriginalAndNotifies()
    {
        var harness = new Harness();
        harness.Keyboard.PasteThrows = true;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.ReplaceFailed, outcome.Status);
        Assert.Equal("選択範囲を置き換えできませんでした", outcome.Notice);
        Assert.Equal(Original, harness.Clipboard.Content);
        Assert.Equal(2, harness.Clipboard.RestoreCount);
        Assert.DoesNotContain(SelectionEditService.RestoreDelay, harness.Delays);
    }

    // --- Clipboard exceptions at each stage (architect spike §2.3a) ---

    [Fact]
    public async Task SnapshotThrowsBeforeCopy_CaptureFailed_NoCopy()
    {
        var harness = new Harness();
        harness.Clipboard.SnapshotThrowsOnCall = 1;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.CaptureFailed, outcome.Status);
        Assert.StartsWith("clipboard_error_snapshot", outcome.Reason);
        Assert.Equal(0, harness.Keyboard.CopyCount);
        Assert.Equal(Original, harness.Clipboard.Content);
    }

    [Fact]
    public async Task ReadThrows_CaptureFailed_RestoresOriginal()
    {
        var harness = new Harness();
        harness.Clipboard.ReadThrows = true;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.CaptureFailed, outcome.Status);
        Assert.StartsWith("clipboard_error_read", outcome.Reason);
        Assert.Empty(harness.Formatter.Requests);
        Assert.Equal(Original, harness.Clipboard.Content);
    }

    [Fact]
    public async Task RestoreAfterCaptureThrows_CaptureFailed_NoFormat()
    {
        var harness = new Harness();
        harness.Clipboard.RestoreThrowsOnCall = 1;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.CaptureFailed, outcome.Status);
        Assert.StartsWith("clipboard_error_restore", outcome.Reason);
        Assert.Empty(harness.Formatter.Requests);
        Assert.Empty(harness.Keyboard.Pasted);
    }

    [Fact]
    public async Task SnapshotThrowsBeforeReplacement_ReplaceFailed_NoPaste()
    {
        var harness = new Harness();
        harness.Clipboard.SnapshotThrowsOnCall = 2;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.ReplaceFailed, outcome.Status);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.Equal(Original, harness.Clipboard.Content);
    }

    [Fact]
    public async Task SetTextThrows_ReplaceFailed_NoPaste()
    {
        var harness = new Harness();
        harness.Clipboard.SetThrows = true;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.ReplaceFailed, outcome.Status);
        Assert.StartsWith("clipboard_error_set", outcome.Reason);
        Assert.Equal("選択範囲を置き換えできませんでした", outcome.Notice);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.DoesNotContain("paste", harness.Clipboard.Events);
        Assert.Equal(Original, harness.Clipboard.Content);
    }

    [Fact]
    public async Task SetTextThrowsOnTargetChangedPath_ReplaceFailed_NoThrow()
    {
        var harness = new Harness();
        harness.Clipboard.SetThrows = true;
        harness.Formatter = new FakeFormatter(_ =>
        {
            harness.Foreground = OtherWindow;
            return Task.FromResult(new FormatResult(Edited, false, null));
        });

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.ReplaceFailed, outcome.Status);
        Assert.Empty(harness.Keyboard.Pasted);
    }

    [Fact]
    public async Task RestoreAfterPasteThrows_StillReplaced_NoThrow()
    {
        var harness = new Harness();
        harness.Clipboard.RestoreThrowsOnCall = 2;

        var outcome = await harness.RunAsync();

        Assert.Equal(SelectionEditStatus.Replaced, outcome.Status);
        Assert.Equal([Edited], harness.Keyboard.Pasted);
    }

    [Fact]
    public async Task CopySenderThrows_CaptureFailed_NoFormat()
    {
        var harness = new Harness();
        var keyboard = new ThrowingCopyKeyboard();
        var service = new SelectionEditService(
            harness.Clipboard,
            keyboard,
            harness.Formatter,
            new FixedDictionaryProvider(),
            () => TargetWindow,
            () => false,
            () => false,
            new AppSettings { Llm = new LlmSettings { Enabled = true } },
            (_, _) => Task.CompletedTask);

        var outcome = await service.RunAsync();

        Assert.Equal(SelectionEditStatus.CaptureFailed, outcome.Status);
        Assert.Empty(harness.Formatter.Requests);
        Assert.Equal(Original, harness.Clipboard.Content);
    }

    // --- Privacy ---

    [Fact]
    public async Task EventLogAndDebugOutput_NeverContainSelectedOrEditedText()
    {
        using var listener = new CollectingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            var harnesses = new[]
            {
                new Harness(),
                new Harness { LlmEnabled = false },
                FailingFormatterHarness(),
                TargetChangedHarness(),
            };
            foreach (var harness in harnesses)
            {
                await harness.RunAsync();
            }

            var entries = harnesses.SelectMany(harness => harness.EventLog.Entries).ToList();
            Assert.Equal(harnesses.Length, entries.Count);
            Assert.All(entries, entry => Assert.Equal("selection_edit", entry.Stage));
            Assert.Contains(entries, entry => entry.Success && entry.CharCount == Selected.Length);

            // Guards the scan itself: the service's debug output reaches the listener.
            Assert.Contains("[SelectionEditService] status=Replaced reason=replaced", listener.Text);

            var serialized = JsonSerializer.Serialize(entries) + listener.Text;
            foreach (var secret in new[] { Selected, Edited, Original })
            {
                Assert.DoesNotContain(secret, serialized);
                Assert.DoesNotContain(JsonSerializer.Serialize(secret).Trim('"'), serialized);
            }
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    private static Harness FailingFormatterHarness()
    {
        var harness = new Harness();
        harness.Formatter = new FakeFormatter(request =>
            Task.FromResult(new FormatResult(request.RawText, true, "timeout", Edited)));
        return harness;
    }

    private static Harness TargetChangedHarness()
    {
        var harness = new Harness();
        harness.Formatter = new FakeFormatter(_ =>
        {
            harness.Foreground = OtherWindow;
            return Task.FromResult(new FormatResult(Edited, false, null));
        });
        return harness;
    }

    private static void AssertNothingTouched(Harness harness, SelectionEditOutcome outcome, SelectionEditStatus status)
    {
        Assert.Equal(status, outcome.Status);
        Assert.Equal(0, harness.Keyboard.CopyCount);
        Assert.Empty(harness.Keyboard.Pasted);
        Assert.Empty(harness.Formatter.Requests);
        Assert.DoesNotContain("snapshot", harness.Clipboard.Events);
        Assert.DoesNotContain("set", harness.Clipboard.Events);
        Assert.DoesNotContain("set+seq", harness.Clipboard.Events);
        Assert.DoesNotContain("restore", harness.Clipboard.Events);
        Assert.Equal(Original, harness.Clipboard.Content);
        Assert.Equal(100u, harness.Clipboard.Sequence);
        Assert.Single(harness.EventLog.Entries);
    }

    private sealed class ThrowingCopyKeyboard : ISelectionKeyboard
    {
        public Task<bool> WaitForModifierReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public void SendCopy() => throw new InvalidOperationException("SendInput failed");

        public void SendPaste() => throw new InvalidOperationException("not expected");
    }

    private sealed class CollectingTraceListener : TraceListener
    {
        private readonly System.Text.StringBuilder _text = new();
        private readonly object _sync = new();

        public string Text
        {
            get
            {
                lock (_sync)
                {
                    return _text.ToString();
                }
            }
        }

        public override void Write(string? message)
        {
            lock (_sync)
            {
                _text.Append(message);
            }
        }

        public override void WriteLine(string? message)
        {
            lock (_sync)
            {
                _text.AppendLine(message);
            }
        }
    }
}
