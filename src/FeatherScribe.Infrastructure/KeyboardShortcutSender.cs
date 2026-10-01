using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// Real keyboard access for selected-text editing: waits until the user has released the hotkey's modifier
/// keys, then sends Ctrl+C / Ctrl+V with SendInput (the same sender as <see cref="ClipboardTextOutput"/>).
/// Stateless; never used by unit tests.
/// </summary>
public sealed class KeyboardShortcutSender
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);

    // Ctrl, Shift, Alt and both Win keys: any of them still held would turn Ctrl+C into another shortcut.
    private static readonly int[] ModifierVirtualKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C];

    /// <summary>
    /// Polls GetAsyncKeyState every 15 ms until no modifier is physically held. Returns false when a modifier
    /// is still held after <paramref name="timeout"/>.
    /// </summary>
    public async Task<bool> WaitForModifierReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (!IsAnyModifierHeld())
            {
                return true;
            }

            if (stopwatch.Elapsed >= timeout)
            {
                return false;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Releases the modifiers, then sends Ctrl+C to the foreground window.</summary>
    public void SendCopy() => KeyboardInput.SendCtrlC();

    /// <summary>Releases the modifiers, then sends Ctrl+V to the foreground window.</summary>
    public void SendPaste() => KeyboardInput.SendCtrlV();

    private static bool IsAnyModifierHeld()
        => ModifierVirtualKeys.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
