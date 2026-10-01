using System.Diagnostics;
using System.Runtime.InteropServices;
using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// クリップボードへコピーし、必要なら Ctrl+V をアクティブウィンドウへ送信する。
/// クリップボードは他プロセスがロックしている場合があるためリトライする。
/// output.restoreClipboard が true の場合、貼り付け後に元のクリップボード内容を復元する
/// (クリップボードがその間に他から変更されていれば復元しない)。
/// </summary>
public sealed class ClipboardTextOutput : ITextOutput
{
    /// <summary>Paste boundary: how long the target app gets to read the clipboard before it is restored.</summary>
    internal static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(600);

    private readonly OutputSettings _settings;
    private readonly IClipboardAccess _clipboard;
    private readonly Action _sendPaste;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    // Only used when restoreClipboard is on: a second output must not capture FeatherScribe's previous
    // text as the "original" while that output's restore is still pending.
    private readonly SemaphoreSlim _restoreGate = new(1, 1);

    public ClipboardTextOutput(OutputSettings settings)
        : this(settings, new WpfClipboardAccess(), KeyboardInput.SendCtrlV, Task.Delay)
    {
    }

    internal ClipboardTextOutput(
        OutputSettings settings,
        IClipboardAccess clipboard,
        Action sendPaste,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(sendPaste);
        ArgumentNullException.ThrowIfNull(delay);
        _settings = settings;
        _clipboard = clipboard;
        _sendPaste = sendPaste;
        _delay = delay;
    }

    public async Task OutputAsync(string text, OutputMode mode, CancellationToken cancellationToken)
    {
        if (!_settings.RestoreClipboard)
        {
            // Existing behavior, unchanged: set text, wait pasteDelay, Ctrl+V. No capture, no lock, no restore.
            await _clipboard.SetTextAsync(text).ConfigureAwait(false);

            if (mode == OutputMode.ClipboardAndPaste)
            {
                await _delay(PasteDelay, cancellationToken).ConfigureAwait(false);
                _sendPaste();
            }

            return;
        }

        await _restoreGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ClipboardRestorePolicy.ShouldCapture(_settings.RestoreClipboard, mode))
            {
                // ClipboardOnly: the text is meant to stay on the clipboard.
                await _clipboard.SetTextAsync(text).ConfigureAwait(false);
                return;
            }

            await OutputWithRestoreAsync(text, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _restoreGate.Release();
        }
    }

    private TimeSpan PasteDelay => TimeSpan.FromMilliseconds(Math.Max(0, _settings.PasteDelayMilliseconds));

    private async Task OutputWithRestoreAsync(string text, CancellationToken cancellationToken)
    {
        var snapshot = await _clipboard.CaptureSnapshotAsync().ConfigureAwait(false);
        if (snapshot.Kind == SnapshotKind.Unsupported)
        {
            Log($"clipboard_snapshot_unsupported reason={snapshot.UnsupportedReason}");
        }

        var expectedSequence = await _clipboard.SetTextAndGetSequenceAsync(text).ConfigureAwait(false);

        try
        {
            await _delay(PasteDelay, cancellationToken).ConfigureAwait(false);
            _sendPaste();
        }
        catch (Exception ex)
        {
            // The text was not pasted: give the user their clipboard back right away, then report the failure.
            Log($"clipboard_paste_failed error={ex.GetType().Name}");
            await TryRestoreAsync(snapshot, expectedSequence, pasteSent: false).ConfigureAwait(false);
            throw;
        }

        if (snapshot.Kind == SnapshotKind.Unsupported)
        {
            // Nothing to restore: keep the existing behavior for this run (no extra wait).
            Log("clipboard_restore_skipped_unsupported");
            return;
        }

        // The paste already happened, so the restore must not be abandoned on cancellation.
        await _delay(RestoreDelay, CancellationToken.None).ConfigureAwait(false);
        await TryRestoreAsync(snapshot, expectedSequence, pasteSent: true).ConfigureAwait(false);
    }

    private async Task TryRestoreAsync(ClipboardSnapshot snapshot, uint expectedSequence, bool pasteSent)
    {
        try
        {
            var decision = ClipboardRestorePolicy.Decide(
                pasteSent,
                expectedSequence,
                _clipboard.GetSequenceNumber(),
                snapshot.Kind);

            switch (decision)
            {
                case RestoreDecision.RestoreNow:
                    var restored = await _clipboard.RestoreAsync(snapshot, expectedSequence).ConfigureAwait(false);
                    Log(restored
                        ? $"clipboard_restored kind={snapshot.Kind} formats={snapshot.FormatCount} pasteSent={pasteSent}"
                        : "clipboard_restore_skipped_changed");
                    break;
                case RestoreDecision.SkipChanged:
                    Log("clipboard_restore_skipped_changed");
                    break;
                default:
                    Log("clipboard_restore_skipped_unsupported");
                    break;
            }
        }
        catch (Exception ex)
        {
            // A failed restore must not turn a successful paste into an error (or mask the paste error).
            Log($"clipboard_restore_failed error={ex.GetType().Name}");
        }
    }

    // Metadata only: decision, kind, format count, exception type. Never clipboard content.
    private static void Log(string message) => Debug.WriteLine($"[ClipboardTextOutput] {message}");
}

/// <summary>SendInput による Ctrl+V / Ctrl+C 送信。</summary>
internal static class KeyboardInput
{
    private const int InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;

    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12; // Alt
    private const ushort VkLWin = 0x5B;
    private const ushort VkC = 0x43;
    private const ushort VkV = 0x56;

    public static void SendCtrlV() => SendCtrlChord(VkV, "Ctrl+V");

    public static void SendCtrlC() => SendCtrlChord(VkC, "Ctrl+C");

    private static void SendCtrlChord(ushort key, string name)
    {
        // ホットキー由来の修飾キーが押されたままだと Ctrl+V が化けるため、先に解放を送る
        var inputs = new[]
        {
            KeyUp(VkShift),
            KeyUp(VkMenu),
            KeyUp(VkLWin),
            KeyUp(VkControl),
            KeyDown(VkControl),
            KeyDown(key),
            KeyUp(key),
            KeyUp(VkControl),
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"{name} の送信に失敗しました (SendInput: {sent}/{inputs.Length}, Win32Error: {Marshal.GetLastWin32Error()})");
        }
    }

    private static Input KeyDown(ushort virtualKey) => new()
    {
        Type = InputKeyboard,
        Data = new KeyboardInputData { VirtualKey = virtualKey },
    };

    private static Input KeyUp(ushort virtualKey) => new()
    {
        Type = InputKeyboard,
        Data = new KeyboardInputData { VirtualKey = virtualKey, Flags = KeyEventFKeyUp },
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public KeyboardInputData Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
        // INPUT 共用体の MOUSEINPUT とサイズを合わせるためのパディング
        private readonly uint _padding1;
        private readonly uint _padding2;
    }
}
