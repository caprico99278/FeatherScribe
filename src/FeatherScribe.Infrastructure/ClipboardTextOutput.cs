using System.Runtime.InteropServices;
using FeatherScribe.Core;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// クリップボードへコピーし、必要なら Ctrl+V をアクティブウィンドウへ送信する。
/// クリップボードは他プロセスがロックしている場合があるためリトライする。
/// </summary>
public sealed class ClipboardTextOutput : ITextOutput
{
    private readonly OutputSettings _settings;

    public ClipboardTextOutput(OutputSettings settings)
    {
        _settings = settings;
    }

    public async Task OutputAsync(string text, OutputMode mode, CancellationToken cancellationToken)
    {
        await RunOnStaThreadAsync(() => SetClipboardTextWithRetry(text)).ConfigureAwait(false);

        if (mode == OutputMode.ClipboardAndPaste)
        {
            await Task.Delay(Math.Max(0, _settings.PasteDelayMilliseconds), cancellationToken)
                .ConfigureAwait(false);
            KeyboardInput.SendCtrlV();
        }
    }

    private static void SetClipboardTextWithRetry(string text)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (COMException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static Task RunOnStaThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>SendInput による Ctrl+V 送信。</summary>
internal static class KeyboardInput
{
    private const int InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;

    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12; // Alt
    private const ushort VkLWin = 0x5B;
    private const ushort VkV = 0x56;

    public static void SendCtrlV()
    {
        // ホットキー由来の修飾キーが押されたままだと Ctrl+V が化けるため、先に解放を送る
        var inputs = new[]
        {
            KeyUp(VkShift),
            KeyUp(VkMenu),
            KeyUp(VkLWin),
            KeyUp(VkControl),
            KeyDown(VkControl),
            KeyDown(VkV),
            KeyUp(VkV),
            KeyUp(VkControl),
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"Ctrl+V の送信に失敗しました (SendInput: {sent}/{inputs.Length}, Win32Error: {Marshal.GetLastWin32Error()})");
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
