using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.Tests;

/// <summary>
/// Orchestration of output.restoreClipboard with a fake clipboard, a fake paste sender and a fake delay.
/// Never touches the real clipboard and never sends real keystrokes.
/// </summary>
public sealed class ClipboardTextOutputTests
{
    private sealed class FakeClipboard : IClipboardAccess
    {
        private readonly object _sync = new();
        private readonly List<string> _events = [];

        public uint Sequence { get; private set; } = 100;
        public ClipboardSnapshot SnapshotToCapture { get; set; } = ClipboardSnapshot.Captured(new object(), 2);
        public List<ClipboardSnapshot> Restored { get; } = [];
        public bool RestoreThrows { get; set; }

        /// <summary>Simulates another app copying between the sequence check and the restore write.</summary>
        public bool ChangeRightAfterSequenceRead { get; set; }

        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_sync)
                {
                    return [.. _events];
                }
            }
        }

        public void Record(string entry)
        {
            lock (_sync)
            {
                _events.Add(entry);
            }
        }

        public void ExternalChange()
        {
            Record("external-change");
            Sequence++;
        }

        public Task<ClipboardSnapshot> CaptureSnapshotAsync()
        {
            Record("capture");
            return Task.FromResult(SnapshotToCapture);
        }

        public Task SetTextAsync(string text)
        {
            Record($"set:{text}");
            Sequence++;
            return Task.CompletedTask;
        }

        public Task<uint> SetTextAndGetSequenceAsync(string text)
        {
            Record($"set+seq:{text}");
            Sequence++;
            return Task.FromResult(Sequence);
        }

        public uint GetSequenceNumber()
        {
            Record("seq");
            var current = Sequence;
            if (ChangeRightAfterSequenceRead)
            {
                ExternalChange();
            }

            return current;
        }

        public Task<bool> RestoreAsync(ClipboardSnapshot snapshot, uint expectedSequence)
        {
            if (RestoreThrows)
            {
                Record("restore-throws");
                throw new System.Runtime.InteropServices.COMException("clipboard locked");
            }

            if (Sequence != expectedSequence)
            {
                Record("restore-refused");
                return Task.FromResult(false);
            }

            Record("restore");
            Restored.Add(snapshot);
            Sequence++;
            return Task.FromResult(true);
        }

        // Not used by ClipboardTextOutput (selected-text editing only).
        public Task<CapturedSelection> ReadSelectionAsync()
        {
            Record("read-selection");
            return Task.FromResult(new CapturedSelection(null, IsFromEmptySelection: false));
        }
    }

    private sealed class Harness
    {
        public FakeClipboard Clipboard { get; } = new();
        public List<TimeSpan> Delays { get; } = [];
        public Func<TimeSpan, CancellationToken, Task>? OnDelay { get; set; }
        public Action? OnPaste { get; set; }

        public ClipboardTextOutput Create(bool restoreClipboard, int pasteDelayMilliseconds = 150) =>
            new(
                new OutputSettings
                {
                    RestoreClipboard = restoreClipboard,
                    PasteDelayMilliseconds = pasteDelayMilliseconds,
                },
                Clipboard,
                () =>
                {
                    Clipboard.Record("paste");
                    OnPaste?.Invoke();
                },
                (delay, cancellationToken) =>
                {
                    lock (Delays)
                    {
                        Delays.Add(delay);
                    }

                    Clipboard.Record($"delay:{delay.TotalMilliseconds}");
                    return OnDelay?.Invoke(delay, cancellationToken) ?? Task.CompletedTask;
                });
    }

    [Fact]
    public async Task RestoreDisabled_ClipboardAndPaste_KeepsExistingSequence()
    {
        var harness = new Harness();
        var output = harness.Create(restoreClipboard: false);

        await output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);

        Assert.Equal(["set:A", "delay:150", "paste"], harness.Clipboard.Events);
        Assert.Equal([TimeSpan.FromMilliseconds(150)], harness.Delays);
        Assert.Empty(harness.Clipboard.Restored);
    }

    [Fact]
    public async Task RestoreDisabled_ClipboardOnly_OnlySetsText()
    {
        var harness = new Harness();
        var output = harness.Create(restoreClipboard: false);

        await output.OutputAsync("A", OutputMode.ClipboardOnly, CancellationToken.None);

        Assert.Equal(["set:A"], harness.Clipboard.Events);
        Assert.Empty(harness.Delays);
    }

    [Fact]
    public async Task RestoreDisabled_NegativePasteDelay_IsClampedToZero()
    {
        var harness = new Harness();
        var output = harness.Create(restoreClipboard: false, pasteDelayMilliseconds: -5);

        await output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);

        Assert.Equal([TimeSpan.Zero], harness.Delays);
    }

    [Fact]
    public async Task RestoreDisabled_PasteFailure_PropagatesWithoutRestore()
    {
        var harness = new Harness { OnPaste = () => throw new InvalidOperationException("SendInput failed") };
        var output = harness.Create(restoreClipboard: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None));

        Assert.Equal(["set:A", "delay:150", "paste"], harness.Clipboard.Events);
        Assert.Empty(harness.Clipboard.Restored);
    }

    [Fact]
    public async Task RestoreEnabled_SequenceUnchanged_RestoresSnapshotOnceAfterPasteBoundary()
    {
        var harness = new Harness();
        var snapshot = harness.Clipboard.SnapshotToCapture;
        var output = harness.Create(restoreClipboard: true);

        await output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);

        Assert.Equal(
            ["capture", "set+seq:A", "delay:150", "paste", "delay:600", "seq", "restore"],
            harness.Clipboard.Events);
        Assert.Equal([TimeSpan.FromMilliseconds(150), ClipboardTextOutput.RestoreDelay], harness.Delays);
        Assert.Same(snapshot, Assert.Single(harness.Clipboard.Restored));
    }

    [Fact]
    public async Task RestoreEnabled_ClipboardChangedDuringRestoreDelay_DoesNotRestore()
    {
        var harness = new Harness();
        harness.OnDelay = (delay, _) =>
        {
            if (delay == ClipboardTextOutput.RestoreDelay)
            {
                harness.Clipboard.ExternalChange();
            }

            return Task.CompletedTask;
        };
        var output = harness.Create(restoreClipboard: true);

        await output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);

        Assert.Empty(harness.Clipboard.Restored);
        Assert.DoesNotContain("restore", harness.Clipboard.Events);
    }

    [Fact]
    public async Task RestoreEnabled_ClipboardChangedRightBeforeWrite_RestoreRefusedWithoutError()
    {
        var harness = new Harness();
        harness.Clipboard.ChangeRightAfterSequenceRead = true;
        var output = harness.Create(restoreClipboard: true);

        await output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);

        Assert.Empty(harness.Clipboard.Restored);
        Assert.Contains("restore-refused", harness.Clipboard.Events);
    }

    [Fact]
    public async Task RestoreEnabled_PasteSenderThrows_RestoresImmediatelyAndRethrows()
    {
        var harness = new Harness { OnPaste = () => throw new InvalidOperationException("SendInput failed") };
        var snapshot = harness.Clipboard.SnapshotToCapture;
        var output = harness.Create(restoreClipboard: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None));

        Assert.Equal("SendInput failed", ex.Message);
        Assert.Equal(
            ["capture", "set+seq:A", "delay:150", "paste", "seq", "restore"],
            harness.Clipboard.Events);
        Assert.Same(snapshot, Assert.Single(harness.Clipboard.Restored));
        Assert.DoesNotContain(ClipboardTextOutput.RestoreDelay, harness.Delays);
    }

    [Fact]
    public async Task RestoreEnabled_PasteSenderThrowsAfterClipboardChanged_DoesNotRestoreAndRethrows()
    {
        var harness = new Harness();
        harness.OnPaste = () =>
        {
            harness.Clipboard.ExternalChange();
            throw new InvalidOperationException("SendInput failed");
        };
        var output = harness.Create(restoreClipboard: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None));

        Assert.Empty(harness.Clipboard.Restored);
    }

    [Fact]
    public async Task RestoreEnabled_PasteDelayCanceled_RestoresAndRethrowsCancellation()
    {
        var harness = new Harness();
        harness.OnDelay = (delay, _) => delay == ClipboardTextOutput.RestoreDelay
            ? Task.CompletedTask
            : Task.FromCanceled(new CancellationToken(canceled: true));
        var output = harness.Create(restoreClipboard: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None));

        Assert.DoesNotContain("paste", harness.Clipboard.Events);
        Assert.Single(harness.Clipboard.Restored);
    }

    [Fact]
    public async Task RestoreEnabled_RestoreThrows_PasteStillReportedAsSuccess()
    {
        var harness = new Harness();
        harness.Clipboard.RestoreThrows = true;
        var output = harness.Create(restoreClipboard: true);

        await output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);

        Assert.Contains("paste", harness.Clipboard.Events);
        Assert.Contains("restore-throws", harness.Clipboard.Events);
    }

    [Fact]
    public async Task RestoreEnabled_RestoreThrowsAfterPasteFailure_OriginalErrorIsRethrown()
    {
        var harness = new Harness { OnPaste = () => throw new InvalidOperationException("SendInput failed") };
        harness.Clipboard.RestoreThrows = true;
        var output = harness.Create(restoreClipboard: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None));

        Assert.Equal("SendInput failed", ex.Message);
    }

    [Fact]
    public async Task RestoreEnabled_ClipboardOnly_NeverCapturesOrRestores()
    {
        var harness = new Harness();
        var output = harness.Create(restoreClipboard: true);

        await output.OutputAsync("A", OutputMode.ClipboardOnly, CancellationToken.None);

        Assert.Equal(["set:A"], harness.Clipboard.Events);
        Assert.Empty(harness.Delays);
        Assert.Empty(harness.Clipboard.Restored);
    }

    [Fact]
    public async Task RestoreEnabled_UnsupportedSnapshot_KeepsExistingBehaviorWithoutExtraWait()
    {
        var harness = new Harness();
        harness.Clipboard.SnapshotToCapture = ClipboardSnapshot.Unsupported(ClipboardSnapshot.ReasonUnsupportedFormats);
        var output = harness.Create(restoreClipboard: true);

        await output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);

        Assert.Equal(["capture", "set+seq:A", "delay:150", "paste"], harness.Clipboard.Events);
        Assert.Equal([TimeSpan.FromMilliseconds(150)], harness.Delays);
        Assert.Empty(harness.Clipboard.Restored);
    }

    [Fact]
    public async Task RestoreEnabled_UnsupportedSnapshotAndPasteFails_DoesNotRestore()
    {
        var harness = new Harness { OnPaste = () => throw new InvalidOperationException("SendInput failed") };
        harness.Clipboard.SnapshotToCapture = ClipboardSnapshot.Unsupported(ClipboardSnapshot.ReasonTooLarge);
        var output = harness.Create(restoreClipboard: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None));

        Assert.Empty(harness.Clipboard.Restored);
    }

    [Fact]
    public async Task RestoreEnabled_EmptySnapshot_RestoresEmptyClipboard()
    {
        var harness = new Harness();
        harness.Clipboard.SnapshotToCapture = ClipboardSnapshot.Empty;
        var output = harness.Create(restoreClipboard: true);

        await output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);

        var restored = Assert.Single(harness.Clipboard.Restored);
        Assert.Equal(SnapshotKind.Empty, restored.Kind);
    }

    [Fact]
    public async Task RestoreEnabled_ConcurrentOutputs_SecondCaptureHappensAfterFirstRestore()
    {
        var harness = new Harness();
        var releaseFirstRestoreDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreDelayCount = 0;
        harness.OnDelay = (delay, _) =>
            delay == ClipboardTextOutput.RestoreDelay && Interlocked.Increment(ref restoreDelayCount) == 1
                ? releaseFirstRestoreDelay.Task
                : Task.CompletedTask;
        var output = harness.Create(restoreClipboard: true);

        var first = output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);
        var second = output.OutputAsync("B", OutputMode.ClipboardAndPaste, CancellationToken.None);

        // While the first output's restore is pending, the second must not touch the clipboard.
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, harness.Clipboard.Events.Count(e => e == "capture"));
        Assert.DoesNotContain("set+seq:B", harness.Clipboard.Events);

        releaseFirstRestoreDelay.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(
            [
                "capture", "set+seq:A", "delay:150", "paste", "delay:600", "seq", "restore",
                "capture", "set+seq:B", "delay:150", "paste", "delay:600", "seq", "restore",
            ],
            harness.Clipboard.Events);
        Assert.Equal(2, harness.Clipboard.Restored.Count);
    }

    [Fact]
    public async Task RestoreEnabled_ClipboardOnlyWaitsForPendingRestore()
    {
        var harness = new Harness();
        var releaseRestoreDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnDelay = (delay, _) => delay == ClipboardTextOutput.RestoreDelay
            ? releaseRestoreDelay.Task
            : Task.CompletedTask;
        var output = harness.Create(restoreClipboard: true);

        var paste = output.OutputAsync("A", OutputMode.ClipboardAndPaste, CancellationToken.None);
        var copy = output.OutputAsync("B", OutputMode.ClipboardOnly, CancellationToken.None);

        Assert.False(copy.IsCompleted);
        Assert.DoesNotContain("set:B", harness.Clipboard.Events);

        releaseRestoreDelay.SetResult();
        await Task.WhenAll(paste, copy).WaitAsync(TimeSpan.FromSeconds(10));

        // The explicit copy lands after the restore, so the user ends up with the copied text.
        Assert.Equal("set:B", harness.Clipboard.Events[^1]);
        Assert.Single(harness.Clipboard.Restored);
    }
}
