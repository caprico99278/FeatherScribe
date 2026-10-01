using System.Diagnostics;
using System.Runtime.InteropServices;
using FeatherScribe.Core;
using FeatherScribe.Infrastructure;

namespace FeatherScribe.App;

/// <summary>Keyboard access used by <see cref="SelectionEditService"/> (a fake in unit tests).</summary>
public interface ISelectionKeyboard
{
    /// <summary>True once no modifier key is held; false when one is still held after <paramref name="timeout"/>.</summary>
    Task<bool> WaitForModifierReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sends Ctrl+C to the foreground window. Throws when the input cannot be sent.</summary>
    void SendCopy();

    /// <summary>Sends Ctrl+V to the foreground window. Throws when the input cannot be sent.</summary>
    void SendPaste();
}

/// <summary>The real keyboard: <see cref="KeyboardShortcutSender"/> (GetAsyncKeyState / SendInput).</summary>
public sealed class SystemSelectionKeyboard : ISelectionKeyboard
{
    private readonly KeyboardShortcutSender _sender = new();

    public Task<bool> WaitForModifierReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => _sender.WaitForModifierReleaseAsync(timeout, cancellationToken);

    public void SendCopy() => _sender.SendCopy();

    public void SendPaste() => _sender.SendPaste();
}

/// <summary>The system foreground window (thread-agnostic Win32 call).</summary>
public static class SystemForegroundWindow
{
    public static IntPtr Get() => GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

/// <summary>How a selection edit ended. Each value has exactly one user-facing notice.</summary>
public enum SelectionEditStatus
{
    /// <summary>The selection was replaced with the edited text.</summary>
    Replaced,

    /// <summary>LLM formatting is off; nothing was touched.</summary>
    LlmDisabled,

    /// <summary>A dictation or another selection edit is running; nothing was touched.</summary>
    Busy,

    /// <summary>No selection could be captured; the clipboard was restored when it had changed.</summary>
    CaptureFailed,

    /// <summary>Formatting failed, timed out or was rejected; the selection was not changed.</summary>
    EditFailed,

    /// <summary>The input target changed or a newer operation started; the edited text is on the clipboard.</summary>
    TargetChanged,

    /// <summary>The replacement could not be performed; the clipboard was restored where possible.</summary>
    ReplaceFailed,
}

/// <summary>
/// Result of one selection edit: the status, a metadata-only reason code (safe to log, never contains text),
/// the user-facing notice, and the number of selected characters (0 when nothing was captured).
/// </summary>
public sealed record SelectionEditOutcome(SelectionEditStatus Status, string Reason, string Notice, int CharCount)
{
    public bool Succeeded => Status == SelectionEditStatus.Replaced;
}

/// <summary>
/// Selected-text editing (Phase UX-1): copy the selection of the foreground app, transform it with the
/// configured preset mode, and paste the result over the selection, restoring the user's clipboard.
/// Pure orchestration over injected dependencies. Never throws to its caller; every failure ends in a
/// <see cref="SelectionEditOutcome"/> that leaves the target text intact. The selected and edited text is
/// never logged or persisted (log entries carry only status, reason code, duration and character count).
/// </summary>
public sealed class SelectionEditService
{
    public static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromMilliseconds(1000);
    public static readonly TimeSpan SequencePollInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>After the copy changed the clipboard, the source app may still be writing formats.</summary>
    public static readonly TimeSpan CopySettleDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>Paste boundary before the clipboard is restored (same as FUNC-1).</summary>
    public static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(600);

    private readonly IClipboardAccess _clipboard;
    private readonly ISelectionKeyboard _keyboard;
    private readonly ITextFormatter _formatter;
    private readonly IDictionaryProvider _dictionaryProvider;
    private readonly Func<IntPtr> _getForegroundWindow;
    private readonly Func<bool> _isOwnProcessForeground;
    private readonly Func<bool> _isDictationBusy;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IEventLog? _eventLog;
    private readonly bool _llmEnabled;
    private readonly FormattingMode _mode;
    private readonly TimeSpan _captureTimeout;

    private readonly object _gate = new();
    private bool _inProgress;
    private Guid _currentOperationId;

    /// <param name="clipboard">Clipboard access (<see cref="WpfClipboardAccess"/> in production).</param>
    /// <param name="keyboard">Keyboard access (<see cref="SystemSelectionKeyboard"/> in production).</param>
    /// <param name="formatter">The LLM formatter (validator included); <c>UsedFallback</c> means "not edited".</param>
    /// <param name="dictionaryProvider">Personal dictionary passed to the formatter.</param>
    /// <param name="getForegroundWindow">Current foreground window (<see cref="SystemForegroundWindow.Get"/>).</param>
    /// <param name="isOwnProcessForeground">True when FeatherScribe itself is in the foreground.</param>
    /// <param name="isDictationBusy">True while a dictation is recording or processing.</param>
    /// <param name="settings">llm.enabled and the selectionEdit block.</param>
    /// <param name="delay">Delay function (Task.Delay when null; immediate fake in tests).</param>
    /// <param name="eventLog">Metadata-only event log (optional).</param>
    public SelectionEditService(
        IClipboardAccess clipboard,
        ISelectionKeyboard keyboard,
        ITextFormatter formatter,
        IDictionaryProvider dictionaryProvider,
        Func<IntPtr> getForegroundWindow,
        Func<bool> isOwnProcessForeground,
        Func<bool> isDictationBusy,
        AppSettings settings,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IEventLog? eventLog = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(dictionaryProvider);
        ArgumentNullException.ThrowIfNull(getForegroundWindow);
        ArgumentNullException.ThrowIfNull(isOwnProcessForeground);
        ArgumentNullException.ThrowIfNull(isDictationBusy);
        ArgumentNullException.ThrowIfNull(settings);

        _clipboard = clipboard;
        _keyboard = keyboard;
        _formatter = formatter;
        _dictionaryProvider = dictionaryProvider;
        _getForegroundWindow = getForegroundWindow;
        _isOwnProcessForeground = isOwnProcessForeground;
        _isDictationBusy = isDictationBusy;
        _delay = delay ?? Task.Delay;
        _eventLog = eventLog;
        _llmEnabled = settings.Llm.Enabled;

        var selectionEdit = settings.SelectionEdit ?? new SelectionEditSettings();
        _mode = selectionEdit.ParsedMode;
        _captureTimeout = TimeSpan.FromMilliseconds(Math.Clamp(
            selectionEdit.CaptureTimeoutMilliseconds,
            SelectionEditSettings.MinCaptureTimeoutMilliseconds,
            SelectionEditSettings.MaxCaptureTimeoutMilliseconds));
    }

    /// <summary>The preset mode applied to the selection (never NoFormat).</summary>
    public FormattingMode Mode => _mode;

    /// <summary>Raised (on a worker thread) when the selection was captured and formatting starts.</summary>
    public event Action? FormattingStarted;

    /// <summary>
    /// Runs one selection edit. Only one runs at a time: a request while one is running returns Busy and
    /// makes the running one stale, so it no longer replaces (its result stays on the clipboard).
    /// Never throws.
    /// </summary>
    public async Task<SelectionEditOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid();

        bool alreadyRunning;
        lock (_gate)
        {
            _currentOperationId = operationId;
            alreadyRunning = _inProgress;
        }

        if (alreadyRunning)
        {
            return Complete(SelectionEditStatus.Busy, "selection_edit_in_progress", 0, stopwatch);
        }

        if (!_llmEnabled)
        {
            return Complete(SelectionEditStatus.LlmDisabled, "llm_disabled", 0, stopwatch);
        }

        if (Check(_isDictationBusy, failSafe: true))
        {
            return Complete(SelectionEditStatus.Busy, "dictation_busy", 0, stopwatch);
        }

        if (Check(_isOwnProcessForeground, failSafe: true))
        {
            return Complete(SelectionEditStatus.CaptureFailed, "own_window_foreground", 0, stopwatch);
        }

        lock (_gate)
        {
            // Re-checked: another request may have started while the guards above ran.
            alreadyRunning = _inProgress;
            _inProgress = true;
        }

        if (alreadyRunning)
        {
            return Complete(SelectionEditStatus.Busy, "selection_edit_in_progress", 0, stopwatch);
        }

        SelectionEditOutcome outcome;
        try
        {
            outcome = await RunCoreAsync(operationId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Safety net only: every stage handles its own failures.
            outcome = Outcome(SelectionEditStatus.EditFailed, $"unexpected_{ex.GetType().Name}", 0);
        }
        finally
        {
            lock (_gate)
            {
                _inProgress = false;
            }
        }

        return Complete(outcome, stopwatch);
    }

    private async Task<SelectionEditOutcome> RunCoreAsync(Guid operationId, CancellationToken cancellationToken)
    {
        // 1. Target: the window that holds the selection.
        var target = ForegroundWindow();
        if (target == IntPtr.Zero)
        {
            return Outcome(SelectionEditStatus.CaptureFailed, "no_foreground_window", 0);
        }

        // 2. The hotkey's modifiers must be up, otherwise Ctrl+C becomes another shortcut.
        bool released;
        try
        {
            released = await _keyboard
                .WaitForModifierReleaseAsync(ModifierReleaseTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Outcome(SelectionEditStatus.CaptureFailed, $"modifier_wait_failed_{ex.GetType().Name}", 0);
        }

        if (!released)
        {
            return Outcome(SelectionEditStatus.CaptureFailed, "modifier_held", 0);
        }

        if (ForegroundWindow() != target)
        {
            return Outcome(SelectionEditStatus.CaptureFailed, "foreground_changed_before_copy", 0);
        }

        // 3. Capture.
        var capture = await CaptureSelectionAsync(cancellationToken).ConfigureAwait(false);
        if (capture.Failure is { } captureFailure)
        {
            return captureFailure;
        }

        var selection = capture.Text!;

        // 4. Format with the preset mode. UsedFallback (LLM failure, timeout, validator rejection) = not edited.
        RaiseFormattingStarted();
        string edited;
        try
        {
            var request = new FormatRequest(selection, _mode, _dictionaryProvider.Load());
            var result = await _formatter.FormatAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.UsedFallback)
            {
                return Outcome(SelectionEditStatus.EditFailed, "format_fallback", selection.Length);
            }

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                return Outcome(SelectionEditStatus.EditFailed, "format_empty", selection.Length);
            }

            edited = result.Text;
        }
        catch (Exception ex)
        {
            return Outcome(SelectionEditStatus.EditFailed, $"format_failed_{ex.GetType().Name}", selection.Length);
        }

        // 5. Stale / foreground check before the clipboard is touched again.
        if (StaleReason(operationId, target) is { } staleReason)
        {
            return await KeepEditedOnClipboardAsync(edited, staleReason, selection.Length).ConfigureAwait(false);
        }

        // 6. Replace.
        return await ReplaceAsync(operationId, target, edited, selection.Length).ConfigureAwait(false);
    }

    private async Task<(string? Text, SelectionEditOutcome? Failure)> CaptureSelectionAsync(
        CancellationToken cancellationToken)
    {
        ClipboardSnapshot original;
        uint sequenceBefore;
        try
        {
            original = await _clipboard.CaptureSnapshotAsync().ConfigureAwait(false);
            sequenceBefore = _clipboard.GetSequenceNumber();
        }
        catch (Exception ex)
        {
            return (null, Outcome(SelectionEditStatus.CaptureFailed, $"clipboard_error_snapshot_{ex.GetType().Name}", 0));
        }

        if (original.Kind == SnapshotKind.Unsupported)
        {
            // Proceeds like FUNC-1: the original cannot be written back, only metadata is logged.
            Log($"clipboard_snapshot_unsupported reason={original.UnsupportedReason}");
        }

        if (sequenceBefore == 0)
        {
            // Without the sequence number a copy cannot be detected (or told apart from someone else's).
            return (null, Outcome(SelectionEditStatus.CaptureFailed, "clipboard_sequence_unavailable", 0));
        }

        try
        {
            _keyboard.SendCopy();
        }
        catch (Exception ex)
        {
            await RestoreIfChangedAsync(original, sequenceBefore).ConfigureAwait(false);
            return (null, Outcome(SelectionEditStatus.CaptureFailed, $"copy_send_failed_{ex.GetType().Name}", 0));
        }

        // Wait for the copy: the sequence number changes when the target app wrote the clipboard.
        var changed = false;
        try
        {
            var polls = Math.Max(1, (int)Math.Ceiling(_captureTimeout / SequencePollInterval));
            for (var i = 0; i < polls && !changed; i++)
            {
                await _delay(SequencePollInterval, cancellationToken).ConfigureAwait(false);
                changed = _clipboard.GetSequenceNumber() != sequenceBefore;
            }

            if (changed)
            {
                await _delay(CopySettleDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await RestoreIfChangedAsync(original, sequenceBefore).ConfigureAwait(false);
            return (null, Outcome(SelectionEditStatus.CaptureFailed, $"capture_wait_failed_{ex.GetType().Name}", 0));
        }

        if (!changed)
        {
            // Nothing was copied (no selection): the clipboard is untouched, nothing to restore.
            return (null, Outcome(SelectionEditStatus.CaptureFailed, "capture_timeout", 0));
        }

        uint sequenceAfterCopy;
        CapturedSelection captured;
        try
        {
            sequenceAfterCopy = _clipboard.GetSequenceNumber();
            captured = await _clipboard.ReadSelectionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RestoreIfChangedAsync(original, sequenceBefore).ConfigureAwait(false);
            return (null, Outcome(SelectionEditStatus.CaptureFailed, $"clipboard_error_read_{ex.GetType().Name}", 0));
        }

        // Give the user's clipboard back right away; only while it still holds exactly the copy.
        try
        {
            if (original.Kind != SnapshotKind.Unsupported)
            {
                var restored = await _clipboard.RestoreAsync(original, sequenceAfterCopy).ConfigureAwait(false);
                Log(restored ? "clipboard_restored_after_capture" : "clipboard_restore_after_capture_skipped_changed");
            }
        }
        catch (Exception ex)
        {
            return (null, Outcome(SelectionEditStatus.CaptureFailed, $"clipboard_error_restore_{ex.GetType().Name}", 0));
        }

        if (captured.IsFromEmptySelection)
        {
            return (null, Outcome(SelectionEditStatus.CaptureFailed, "empty_selection_line_copy", 0));
        }

        if (string.IsNullOrWhiteSpace(captured.Text))
        {
            return (null, Outcome(SelectionEditStatus.CaptureFailed, "empty_selection", 0));
        }

        return (captured.Text, null);
    }

    private async Task<SelectionEditOutcome> ReplaceAsync(Guid operationId, IntPtr target, string edited, int charCount)
    {
        // FUNC-1 policy: snapshot right before the write. This is the user's original clipboard (restored after
        // the capture), or whatever the user copied since, which then wins over the older original.
        ClipboardSnapshot current;
        uint sequenceBeforeSet;
        try
        {
            current = await _clipboard.CaptureSnapshotAsync().ConfigureAwait(false);
            sequenceBeforeSet = _clipboard.GetSequenceNumber();
        }
        catch (Exception ex)
        {
            return Outcome(SelectionEditStatus.ReplaceFailed, $"clipboard_error_snapshot_replace_{ex.GetType().Name}", charCount);
        }

        uint ourSequence;
        try
        {
            ourSequence = await _clipboard.SetTextAndGetSequenceAsync(edited).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed write may still have changed (emptied) the clipboard: best-effort restore.
            await RestoreIfChangedAsync(current, sequenceBeforeSet).ConfigureAwait(false);
            return Outcome(SelectionEditStatus.ReplaceFailed, $"clipboard_error_set_{ex.GetType().Name}", charCount);
        }

        // Last check right before Ctrl+V. The edited text is already on the clipboard, which is exactly the
        // "target changed" result, so it is kept there.
        if (StaleReason(operationId, target) is { } staleReason)
        {
            return Outcome(SelectionEditStatus.TargetChanged, staleReason, charCount);
        }

        try
        {
            _keyboard.SendPaste();
        }
        catch (Exception ex)
        {
            await TryRestoreAsync(current, ourSequence).ConfigureAwait(false);
            return Outcome(SelectionEditStatus.ReplaceFailed, $"paste_send_failed_{ex.GetType().Name}", charCount);
        }

        // This restore runs regardless of output.restoreClipboard: the capture overwrote the clipboard without
        // the user asking. Only while the clipboard still holds exactly the edited text.
        if (current.Kind != SnapshotKind.Unsupported)
        {
            try
            {
                await _delay(RestoreDelay, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The delay is only a boundary; restore anyway.
            }

            await TryRestoreAsync(current, ourSequence).ConfigureAwait(false);
        }

        return Outcome(SelectionEditStatus.Replaced, "replaced", charCount);
    }

    private async Task<SelectionEditOutcome> KeepEditedOnClipboardAsync(string edited, string reason, int charCount)
    {
        try
        {
            await _clipboard.SetTextAsync(edited).ConfigureAwait(false);
            return Outcome(SelectionEditStatus.TargetChanged, reason, charCount);
        }
        catch (Exception ex)
        {
            return Outcome(SelectionEditStatus.ReplaceFailed, $"{reason}_clipboard_error_set_{ex.GetType().Name}", charCount);
        }
    }

    /// <summary>Null while the operation may still replace; otherwise why it must not.</summary>
    private string? StaleReason(Guid operationId, IntPtr target)
    {
        lock (_gate)
        {
            if (_currentOperationId != operationId)
            {
                return "stale_operation";
            }
        }

        if (Check(_isDictationBusy, failSafe: true))
        {
            return "dictation_started";
        }

        return ForegroundWindow() == target ? null : "foreground_changed";
    }

    /// <summary>Best effort after a failure: restore only when the clipboard changed since <paramref name="sequenceBefore"/>.</summary>
    private async Task RestoreIfChangedAsync(ClipboardSnapshot snapshot, uint sequenceBefore)
    {
        try
        {
            var current = _clipboard.GetSequenceNumber();
            if (current != 0 && current != sequenceBefore)
            {
                await TryRestoreAsync(snapshot, current).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log($"clipboard_restore_failed error={ex.GetType().Name}");
        }
    }

    private async Task TryRestoreAsync(ClipboardSnapshot snapshot, uint expectedSequence)
    {
        if (snapshot.Kind == SnapshotKind.Unsupported || expectedSequence == 0)
        {
            Log("clipboard_restore_skipped_unsupported");
            return;
        }

        try
        {
            var restored = await _clipboard.RestoreAsync(snapshot, expectedSequence).ConfigureAwait(false);
            Log(restored ? "clipboard_restored" : "clipboard_restore_skipped_changed");
        }
        catch (Exception ex)
        {
            Log($"clipboard_restore_failed error={ex.GetType().Name}");
        }
    }

    private IntPtr ForegroundWindow()
    {
        try
        {
            return _getForegroundWindow();
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    private static bool Check(Func<bool> predicate, bool failSafe)
    {
        try
        {
            return predicate();
        }
        catch (Exception)
        {
            return failSafe;
        }
    }

    private void RaiseFormattingStarted()
    {
        try
        {
            FormattingStarted?.Invoke();
        }
        catch (Exception ex)
        {
            Log($"formatting_started_handler_failed error={ex.GetType().Name}");
        }
    }

    private static SelectionEditOutcome Outcome(SelectionEditStatus status, string reason, int charCount)
        => new(status, reason, UserFacingText.SelectionEditNotice(status), charCount);

    private SelectionEditOutcome Complete(SelectionEditStatus status, string reason, int charCount, Stopwatch stopwatch)
        => Complete(Outcome(status, reason, charCount), stopwatch);

    private SelectionEditOutcome Complete(SelectionEditOutcome outcome, Stopwatch stopwatch)
    {
        Log($"status={outcome.Status} reason={outcome.Reason} chars={outcome.CharCount}");
        try
        {
            // Metadata only: status, reason code, duration, mode, character count. Never the text.
            _eventLog?.Write(new PipelineEvent(
                DateTimeOffset.Now,
                "selection_edit",
                outcome.Succeeded,
                outcome.Succeeded ? null : $"{outcome.Status}:{outcome.Reason}",
                stopwatch.ElapsedMilliseconds,
                _mode.ToString(),
                null,
                outcome.CharCount));
        }
        catch (Exception)
        {
            // The log is best effort and must never change the outcome.
        }

        return outcome;
    }

    // Metadata only: status, reason code, counts, exception type. Never clipboard or selection content.
    private static void Log(string message) => Debug.WriteLine($"[SelectionEditService] {message}");
}
